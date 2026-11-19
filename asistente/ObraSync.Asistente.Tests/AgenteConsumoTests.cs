using ObraSync.Asistente.Application;
using ObraSync.Asistente.Infrastructure;

namespace ObraSync.Asistente.Tests;

/// <summary>Modelo que siempre falla con el código pedido y cuenta cuántas veces lo llamaron.</summary>
internal sealed class ModeloQueFalla : IModeloChat
{
    private readonly string _codigo;
    public int Llamadas { get; private set; }
    public ModeloQueFalla(string codigo) => _codigo = codigo;

    public Task<RespuestaModelo> PedirAsync(System.Text.Json.Nodes.JsonArray mensajes,
        System.Text.Json.Nodes.JsonArray? herramientas, int maxTokensSalida, CancellationToken ct)
    {
        Llamadas++;
        throw new ModeloException(_codigo);
    }
}

/// <summary>El agente con el control de consumo real (en memoria) y un reloj controlado.</summary>
public class AgenteConsumoTests
{
    private readonly RelojFalso _reloj = new();

    private ControlConsumoEnMemoria Control(OpcionesConsumo? o = null) =>
        new(o ?? new OpcionesConsumo(), new LogFalso<ControlConsumoEnMemoria>(), _reloj.Leer);

    private static Agente Agente(IModeloChat modelo, IControlConsumo control) => new(
        modelo,
        new HerramientasCatalogo(new BuscadorFalso(), new ObrasFalsas()),
        new HerramientasCompras(new ConsultorFalso(), new ObrasFalsas()),
        new ContextoFalso { Rol = "Capataz" },
        control);

    [Fact]
    public async Task Registra_los_tokens_que_informa_el_modelo()
    {
        var control = Control();
        var modelo = new ModeloFalso(_ => Respuestas.Final("Hola", tokens: 1_234));

        var r = await Agente(modelo, control).ResponderAsync("hola");

        Assert.True(r.Ok);
        Assert.Equal(1_234, control.Estado().TokensUltimoMinuto);
        Assert.Equal(1_234, control.Estado().TokensMes);
    }

    [Fact]
    public async Task Registra_los_tokens_de_cada_vuelta_al_modelo()
    {
        var control = Control();
        var modelo = new ModeloFalso(
            _ => Respuestas.PideHerramienta("{\"texto\":\"cemento\"}", tokens: 400),
            _ => Respuestas.Final("Hay cemento.", tokens: 600));

        var r = await Agente(modelo, control).ResponderAsync("¿hay cemento?");

        Assert.True(r.Ok);
        Assert.Equal(1_000, r.Tokens);
        Assert.Equal(1_000, control.Estado().TokensMes);
    }

    [Fact]
    public async Task Cerca_del_tope_por_minuto_responde_ocupado_SIN_llamar_al_modelo()
    {
        var control = Control();
        control.Registrar(28_000);                      // el pedido estimado (3.000) ya no entra en los 30.000
        var modelo = new ModeloFalso();                 // si lo llamaran, fallaría (no tiene respuestas preparadas)

        var r = await Agente(modelo, control).ResponderAsync("hola");

        Assert.False(r.Ok);
        Assert.Equal("asistente_ocupado", r.Error);
        Assert.Empty(modelo.Pedidos);
    }

    [Fact]
    public async Task Con_el_interruptor_apagado_responde_apagado_SIN_llamar_al_modelo()
    {
        var control = Control(new OpcionesConsumo { Habilitado = false });
        var modelo = new ModeloFalso();

        var r = await Agente(modelo, control).ResponderAsync("hola");

        Assert.Equal("asistente_apagado", r.Error);
        Assert.Empty(modelo.Pedidos);
    }

    [Fact]
    public async Task Ante_un_429_no_reintenta_y_los_pedidos_siguientes_ni_llaman_al_modelo()
    {
        var control = Control();
        var modelo = new ModeloQueFalla("modelo_limite_de_tasa");
        var agente = Agente(modelo, control);

        var primero = await agente.ResponderAsync("hola");
        var segundo = await agente.ResponderAsync("hola otra vez");

        Assert.Equal("modelo_limite_de_tasa", primero.Error);
        Assert.Equal("asistente_ocupado", segundo.Error);
        Assert.Equal(1, modelo.Llamadas);               // una sola llamada: nada de bucles de reintento
    }

    [Fact]
    public async Task Despues_de_la_pausa_por_429_el_asistente_vuelve_a_intentar()
    {
        var control = Control();
        var agente = Agente(new ModeloQueFalla("modelo_limite_de_tasa"), control);
        await agente.ResponderAsync("hola");

        _reloj.Avanzar(TimeSpan.FromSeconds(61));

        Assert.Equal(DecisionConsumo.Permitido, control.Autorizar());
    }

    [Fact]
    public async Task Ante_un_403_por_cuota_apaga_el_asistente_y_no_vuelve_a_llamar_al_modelo()
    {
        var control = Control();
        var modelo = new ModeloQueFalla("modelo_sin_permiso_o_cupo");
        var agente = Agente(modelo, control);

        var primero = await agente.ResponderAsync("hola");
        _reloj.Avanzar(TimeSpan.FromHours(1));
        var segundo = await agente.ResponderAsync("hola otra vez");

        Assert.Equal("modelo_sin_permiso_o_cupo", primero.Error);
        Assert.Equal("asistente_sin_cupo", segundo.Error);
        Assert.Equal(1, modelo.Llamadas);
    }

    [Fact]
    public async Task Un_rol_sin_perfil_no_gasta_ni_consulta_el_cupo()
    {
        var control = Control();
        var agente = new Agente(new ModeloFalso(), new HerramientasCatalogo(new BuscadorFalso(), new ObrasFalsas()),
            null, new ContextoFalso { Rol = "Proveedor" }, control);

        var r = await agente.ResponderAsync("hola");

        Assert.Equal("rol_sin_perfil", r.Error);
        Assert.Equal(0, control.Estado().TokensMes);
    }
}
