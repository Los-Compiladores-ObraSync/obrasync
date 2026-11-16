using Microsoft.Extensions.Logging;
using ObraSync.Asistente.Application;
using ObraSync.Asistente.Infrastructure;

namespace ObraSync.Asistente.Tests;

/// <summary>Reloj que se mueve a mano: así las pruebas de "pasó un minuto" no esperan de verdad.</summary>
internal sealed class RelojFalso
{
    public DateTimeOffset Ahora { get; set; } = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    public void Avanzar(TimeSpan t) => Ahora += t;
    public DateTimeOffset Leer() => Ahora;
}

/// <summary>Logger que guarda lo que se escribió (nivel y texto) para poder revisarlo.</summary>
internal sealed class LogFalso<T> : ILogger<T>
{
    public List<(LogLevel Nivel, string Texto)> Lineas { get; } = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) => Lineas.Add((logLevel, formatter(state, exception)));
}

public class ControlConsumoTests
{
    private readonly RelojFalso _reloj = new();
    private readonly LogFalso<ControlConsumoEnMemoria> _log = new();

    private ControlConsumoEnMemoria Armar(OpcionesConsumo? opciones = null) =>
        new(opciones ?? new OpcionesConsumo(), _log, _reloj.Leer);

    [Fact]
    public void Sin_consumo_previo_se_permite()
    {
        Assert.Equal(DecisionConsumo.Permitido, Armar().Autorizar());
    }

    [Fact]
    public void Si_el_pedido_estimado_no_entra_en_el_minuto_responde_ocupado_sin_llamar_al_modelo()
    {
        // Tope 10.000 por minuto, estimado 6.000 por pedido: con 5.000 ya gastados, el siguiente no entra.
        var control = Armar();
        control.Registrar(5_000);

        Assert.Equal(DecisionConsumo.Ocupado, control.Autorizar());
    }

    [Fact]
    public void Pasado_el_minuto_vuelve_a_permitir()
    {
        var control = Armar();
        control.Registrar(5_000);
        Assert.Equal(DecisionConsumo.Ocupado, control.Autorizar());

        _reloj.Avanzar(TimeSpan.FromSeconds(61));

        Assert.Equal(DecisionConsumo.Permitido, control.Autorizar());
    }

    [Fact]
    public void La_ventana_es_deslizante_y_solo_cuenta_lo_de_los_ultimos_60_segundos()
    {
        var control = Armar(new OpcionesConsumo { TokensEstimadosPorPedido = 1_000 });
        control.Registrar(5_000);
        _reloj.Avanzar(TimeSpan.FromSeconds(40));
        control.Registrar(4_500);                       // total en la ventana: 9.500
        Assert.Equal(DecisionConsumo.Ocupado, control.Autorizar());

        _reloj.Avanzar(TimeSpan.FromSeconds(30));       // los 5.000 de hace 70 s salen de la ventana: quedan 4.500

        Assert.Equal(DecisionConsumo.Permitido, control.Autorizar());
        Assert.Equal(4_500, control.Estado().TokensUltimoMinuto);
    }

    [Fact]
    public void Un_429_del_gateway_de_IA_pausa_el_asistente_un_minuto_y_despues_vuelve()
    {
        var control = Armar();

        control.ReportarLimiteDeTasa();

        Assert.Equal(DecisionConsumo.Ocupado, control.Autorizar());
        Assert.True(control.Estado().EnEspera);
        _reloj.Avanzar(TimeSpan.FromSeconds(59));
        Assert.Equal(DecisionConsumo.Ocupado, control.Autorizar());
        _reloj.Avanzar(TimeSpan.FromSeconds(2));
        Assert.Equal(DecisionConsumo.Permitido, control.Autorizar());
    }

    [Fact]
    public void Un_403_por_cuota_apaga_el_asistente_y_no_se_levanta_solo()
    {
        var control = Armar();

        control.ReportarSinCupo();

        Assert.Equal(DecisionConsumo.SinCupo, control.Autorizar());
        _reloj.Avanzar(TimeSpan.FromHours(5));
        Assert.Equal(DecisionConsumo.SinCupo, control.Autorizar());
        Assert.True(control.Estado().SinCupoPorCuota);
        Assert.Contains(_log.Lineas, l => l.Nivel == LogLevel.Error);
    }

    [Fact]
    public void El_interruptor_apaga_el_asistente()
    {
        var control = Armar(new OpcionesConsumo { Habilitado = false });

        Assert.Equal(DecisionConsumo.Apagado, control.Autorizar());
        Assert.False(control.Estado().Habilitado);
    }

    [Fact]
    public void Avisa_una_sola_vez_al_llegar_al_80_por_ciento_del_tope_mensual()
    {
        var control = Armar(new OpcionesConsumo { TokensPorMes = 1_000_000 });

        control.Registrar(799_999);
        Assert.DoesNotContain(_log.Lineas, l => l.Nivel == LogLevel.Warning);

        control.Registrar(1);                           // justo el 80 %
        control.Registrar(10);                          // más consumo: no repite el aviso

        Assert.Single(_log.Lineas, l => l.Nivel == LogLevel.Warning);
        Assert.True(control.Estado().AvisoMensualEmitido);
        Assert.Equal(80.0, control.Estado().PorcentajeMes, 0);
    }

    [Fact]
    public void Con_el_tope_mensual_agotado_no_se_llama_al_modelo_y_al_cambiar_el_mes_se_reinicia()
    {
        var control = Armar(new OpcionesConsumo { TokensPorMes = 20_000, TokensPorMinuto = 100_000 });
        control.Registrar(20_000);
        Assert.Equal(DecisionConsumo.SinCupo, control.Autorizar());

        _reloj.Ahora = new DateTimeOffset(2026, 12, 1, 0, 0, 1, TimeSpan.Zero);

        Assert.Equal(DecisionConsumo.Permitido, control.Autorizar());
        Assert.Equal(0, control.Estado().TokensMes);
        Assert.False(control.Estado().AvisoMensualEmitido);
    }

    [Fact]
    public void Los_tokens_en_cero_o_negativos_no_se_registran()
    {
        var control = Armar();
        control.Registrar(0);
        control.Registrar(-5);
        Assert.Equal(0, control.Estado().TokensMes);
    }

    [Fact]
    public void Los_registros_no_incluyen_mensajes_ni_tokens_JWT()
    {
        var control = Armar(new OpcionesConsumo { TokensPorMes = 100 });
        control.Registrar(90);
        control.ReportarLimiteDeTasa();
        control.ReportarSinCupo();

        Assert.DoesNotContain(_log.Lineas, l => l.Texto.Contains("Bearer") || l.Texto.Contains("eyJ"));
    }
}
