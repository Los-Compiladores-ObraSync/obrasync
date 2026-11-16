using Microsoft.Extensions.Logging;
using ObraSync.Asistente.Application;

namespace ObraSync.Asistente.Infrastructure;

/// <summary>
/// Valores del control de consumo. Salen de la sección "Consumo" de la configuración (en Docker: Consumo__Habilitado, etc.).
/// Los topes por defecto son los que dijo el profe (12/11): 10.000 tokens por minuto y 18.750.000 por mes, para TODO el grupo.
/// </summary>
public sealed class OpcionesConsumo
{
    /// <summary>Interruptor: en false el asistente responde "no disponible" sin llamar al modelo.</summary>
    public bool Habilitado { get; set; } = true;

    public int TokensPorMinuto { get; set; } = 10_000;

    public long TokensPorMes { get; set; } = 18_750_000;

    /// <summary>
    /// Cuánto se calcula que gasta un pedido, para no arrancarlo si no entra en el minuto.
    /// ES UNA ESTIMACIÓN (la cuenta de #144: ~5.000 a 7.000 por mensaje): hay que reemplazarla por lo medido en los registros.
    /// </summary>
    public int TokensEstimadosPorPedido { get; set; } = 6_000;

    /// <summary>A partir de este porcentaje del tope mensual se avisa en el log (una vez por mes).</summary>
    public double UmbralAviso { get; set; } = 0.8;

    /// <summary>Mensajes por hora que puede mandar cada usuario al chat (valor inicial de #144; se ajusta con Consumo__MensajesPorHoraPorUsuario).</summary>
    public int MensajesPorHoraPorUsuario { get; set; } = 20;

    /// <summary>Segundos que se pausa el asistente después de un 429 del gateway de IA.</summary>
    public int EsperaTrasLimiteSegundos { get; set; } = 60;
}

/// <summary>
/// Control de consumo en memoria del proceso. Sin base de datos (el servicio no tiene una):
/// el acumulado mensual se pierde si el servicio se reinicia. Es suficiente para cuidar el cupo del grupo
/// durante el desarrollo y la demo; el registro por pedido queda en los logs para reconstruirlo.
/// </summary>
public sealed class ControlConsumoEnMemoria : IControlConsumo
{
    private static readonly TimeSpan Ventana = TimeSpan.FromMinutes(1);

    private readonly OpcionesConsumo _opciones;
    private readonly ILogger<ControlConsumoEnMemoria> _log;
    private readonly Func<DateTimeOffset> _reloj;
    private readonly object _candado = new();

    // Los tokens de los últimos 60 segundos (ventana deslizante).
    private readonly Queue<(DateTimeOffset Momento, int Tokens)> _ventana = new();
    private int _mes;                       // año*100 + mes (UTC) al que corresponde _tokensMes
    private long _tokensMes;
    private bool _avisoEmitido;
    private DateTimeOffset _esperaHasta = DateTimeOffset.MinValue;
    private bool _sinCupoPorCuota;

    public ControlConsumoEnMemoria(
        OpcionesConsumo opciones, ILogger<ControlConsumoEnMemoria> log, Func<DateTimeOffset>? reloj = null)
    {
        _opciones = opciones;
        _log = log;
        _reloj = reloj ?? (() => DateTimeOffset.UtcNow);
    }

    public DecisionConsumo Autorizar()
    {
        lock (_candado)
        {
            var ahora = _reloj();
            Actualizar(ahora);

            if (!_opciones.Habilitado)
                return DecisionConsumo.Apagado;
            if (_sinCupoPorCuota || _tokensMes >= _opciones.TokensPorMes)
                return DecisionConsumo.SinCupo;
            if (ahora < _esperaHasta)
                return DecisionConsumo.Ocupado;

            // Si el pedido que viene (estimado) no entra en lo que queda del minuto, se espera sin llamar al modelo.
            if (UsadoEnLaVentana() + _opciones.TokensEstimadosPorPedido > _opciones.TokensPorMinuto)
                return DecisionConsumo.Ocupado;

            return DecisionConsumo.Permitido;
        }
    }

    public void Registrar(int tokens)
    {
        if (tokens <= 0)
            return;

        lock (_candado)
        {
            var ahora = _reloj();
            Actualizar(ahora);
            _ventana.Enqueue((ahora, tokens));
            _tokensMes += tokens;

            var aviso = _opciones.TokensPorMes * _opciones.UmbralAviso;
            if (!_avisoEmitido && _tokensMes >= aviso)
            {
                _avisoEmitido = true;
                _log.LogWarning(
                    "Consumo del asistente: se llegó al {Umbral:P0} del tope mensual ({Usados} de {Tope} tokens). Avisar a Agus.",
                    _opciones.UmbralAviso, _tokensMes, _opciones.TokensPorMes);
            }
        }
    }

    public void ReportarLimiteDeTasa()
    {
        lock (_candado)
        {
            _esperaHasta = _reloj().AddSeconds(_opciones.EsperaTrasLimiteSegundos);
        }
        _log.LogWarning(
            "El gateway de IA respondió 429 (límite por minuto). Se pausa el asistente {Segundos} s y NO se reintenta.",
            _opciones.EsperaTrasLimiteSegundos);
    }

    public void ReportarSinCupo()
    {
        lock (_candado)
        {
            _sinCupoPorCuota = true;
        }
        _log.LogError(
            "El gateway de IA respondió 403 (cuota de costo agotada o sin permiso). El asistente queda APAGADO hasta reiniciar el servicio. Avisar a Agus y al profe.");
    }

    public EstadoConsumo Estado()
    {
        lock (_candado)
        {
            var ahora = _reloj();
            Actualizar(ahora);
            return new EstadoConsumo(
                _opciones.Habilitado,
                _sinCupoPorCuota,
                ahora < _esperaHasta,
                UsadoEnLaVentana(),
                _opciones.TokensPorMinuto,
                _tokensMes,
                _opciones.TokensPorMes,
                _opciones.TokensPorMes == 0 ? 0 : Math.Round(100.0 * _tokensMes / _opciones.TokensPorMes, 2),
                _avisoEmitido);
        }
    }

    /// <summary>Saca de la ventana lo que tiene más de 60 s y reinicia el acumulado si cambió el mes (UTC).</summary>
    private void Actualizar(DateTimeOffset ahora)
    {
        while (_ventana.Count > 0 && ahora - _ventana.Peek().Momento >= Ventana)
            _ventana.Dequeue();

        var mes = ahora.UtcDateTime.Year * 100 + ahora.UtcDateTime.Month;
        if (mes != _mes)
        {
            _mes = mes;
            _tokensMes = 0;
            _avisoEmitido = false;
        }
    }

    private long UsadoEnLaVentana() => _ventana.Sum(x => (long)x.Tokens);
}
