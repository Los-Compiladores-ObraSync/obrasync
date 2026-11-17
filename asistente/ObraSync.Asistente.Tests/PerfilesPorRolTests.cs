using System.Text.Json.Nodes;
using ObraSync.Asistente.Application;

namespace ObraSync.Asistente.Tests;

/// <summary>Perfiles por rol (#143): cada rol recibe sus instrucciones y solo sus herramientas.</summary>
public class PerfilesPorRolTests
{
    /// <summary>El modelo falso no recibe las herramientas: se las pedimos a través de un modelo que las anota.</summary>
    private sealed class ModeloQueAnotaHerramientas : IModeloChat
    {
        private readonly Queue<RespuestaModelo> _respuestas;
        public List<JsonArray?> Herramientas { get; } = new();
        public List<string> Sistema { get; } = new();

        public ModeloQueAnotaHerramientas(params RespuestaModelo[] respuestas) => _respuestas = new(respuestas);

        public Task<RespuestaModelo> PedirAsync(JsonArray mensajes, JsonArray? herramientas, int maxTokensSalida, CancellationToken ct)
        {
            Herramientas.Add((JsonArray?)herramientas?.DeepClone());
            Sistema.Add(mensajes[0]!["content"]!.GetValue<string>());
            return Task.FromResult(_respuestas.Dequeue());
        }

        public IEnumerable<string> NombresOfrecidos() =>
            Herramientas[0]!.Select(d => d!["function"]!["name"]!.GetValue<string>());
    }

    private static Agente ArmarAgente(
        IModeloChat modelo, string? rol, ConsultorFalso? consultor = null, ConsultorRecepcionesFalso? recepciones = null,
        ConsultorLicitacionesFalso? licitaciones = null) => new(
        modelo,
        new HerramientasCatalogo(new BuscadorFalso(), new ObrasFalsas()),
        new HerramientasCompras(consultor ?? new ConsultorFalso(), new ObrasFalsas()),
        new ContextoFalso { Rol = rol },
        null,
        new HerramientasRecepcion(recepciones ?? new ConsultorRecepcionesFalso(), new ObrasFalsas()),
        new HerramientasLicitaciones(licitaciones ?? new ConsultorLicitacionesFalso(), new ObrasFalsas()));

    [Theory]
    [InlineData("Capataz", new[] { "buscar_insumos", "armar_borrador_requisicion", "listar_requisiciones", "ver_requisicion", "listar_ordenes_pendientes", "preparar_recepcion" })]
    [InlineData("Comprador", new[] { "listar_requisiciones", "ver_requisicion", "preparar_aprobacion_requisicion", "preparar_rechazo_requisicion", "preparar_publicacion_licitacion", "listar_licitaciones", "ver_licitacion", "listar_cotizaciones", "preparar_adjudicacion" })]
    [InlineData("Admin", new[] { "buscar_insumos", "armar_borrador_requisicion", "listar_requisiciones", "ver_requisicion", "listar_ordenes_pendientes", "preparar_recepcion", "preparar_aprobacion_requisicion", "preparar_rechazo_requisicion", "preparar_publicacion_licitacion", "listar_licitaciones", "ver_licitacion", "listar_cotizaciones", "preparar_adjudicacion" })]
    public async Task Cada_rol_ve_solo_las_herramientas_de_su_perfil(string rol, string[] esperadas)
    {
        var modelo = new ModeloQueAnotaHerramientas(Respuestas.Final("Hola"));

        var r = await ArmarAgente(modelo, rol).ResponderAsync("hola");

        Assert.True(r.Ok);
        Assert.Equal(esperadas.OrderBy(x => x), modelo.NombresOfrecidos().OrderBy(x => x));
    }

    [Fact]
    public async Task El_Comprador_recibe_las_instrucciones_de_Compras_y_no_las_del_Capataz()
    {
        var modelo = new ModeloQueAnotaHerramientas(Respuestas.Final("Hola"));

        await ArmarAgente(modelo, "Comprador").ResponderAsync("hola");

        Assert.Equal(PerfilAsistente.InstruccionesCompras, modelo.Sistema[0]);
        Assert.DoesNotContain("armar_borrador_requisicion", modelo.Sistema[0]);
    }

    [Fact]
    public async Task El_Capataz_sigue_recibiendo_las_instrucciones_de_siempre()
    {
        var modelo = new ModeloQueAnotaHerramientas(Respuestas.Final("Hola"));

        await ArmarAgente(modelo, "Capataz").ResponderAsync("hola");

        Assert.Equal(Agente.Instrucciones, modelo.Sistema[0]);
    }

    [Fact]
    public async Task Sin_contexto_se_atiende_como_Capataz_igual_que_en_la_primera_version()
    {
        var modelo = new ModeloQueAnotaHerramientas(Respuestas.Final("Hola"));
        var agente = new Agente(modelo, new HerramientasCatalogo(new BuscadorFalso(), new ObrasFalsas()));

        await agente.ResponderAsync("hola");

        Assert.Equal(
            new[] { "buscar_insumos", "armar_borrador_requisicion", "listar_requisiciones", "ver_requisicion", "listar_ordenes_pendientes", "preparar_recepcion" }.OrderBy(x => x),
            modelo.NombresOfrecidos().OrderBy(x => x));
    }

    [Theory]
    [InlineData("Proveedor")]
    [InlineData(null)]
    [InlineData("")]
    public async Task Un_rol_sin_perfil_no_llega_al_modelo(string? rol)
    {
        var modelo = new ModeloFalso();

        var r = await ArmarAgente(modelo, rol).ResponderAsync("hola");

        Assert.False(r.Ok);
        Assert.Equal("rol_sin_perfil", r.Error);
        Assert.Empty(modelo.Pedidos);
    }

    [Fact]
    public async Task El_Capataz_puede_consultar_sus_requisiciones_y_el_modelo_recibe_el_resultado()
    {
        var consultor = new ConsultorFalso();
        var modelo = new ModeloFalso(
            _ => Respuestas.PideHerramienta("{\"estado\":\"Pendiente\"}", nombre: "listar_requisiciones"),
            _ => Respuestas.Final("Tenés 2 requisiciones pendientes."));

        var r = await ArmarAgente(modelo, "Capataz", consultor).ResponderAsync("¿en qué quedó mi requisición?");

        Assert.True(r.Ok);
        Assert.Equal(1, consultor.LlamadasLista);
        Assert.Equal("Pendiente", consultor.UltimoEstado);
        Assert.Contains("REQ-0001", modelo.Pedidos[1].Last()!["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task El_Capataz_no_puede_preparar_una_aprobacion_aunque_pueda_consultar()
    {
        var consultor = new ConsultorFalso();
        var modelo = new ModeloFalso(
            _ => Respuestas.PideHerramienta("{\"requisicionId\":\"" + ConsultorFalso.IdPendiente + "\"}", nombre: "preparar_aprobacion_requisicion"),
            _ => Respuestas.Final("No puedo."));

        var r = await ArmarAgente(modelo, "Capataz", consultor).ResponderAsync("aprobá mi requisición");

        Assert.True(r.Ok);
        Assert.Equal(0, consultor.LlamadasDetalle);
        Assert.Contains("herramienta_no_permitida", modelo.Pedidos[1].Last()!["content"]!.GetValue<string>());
        Assert.Null(r.Aprobacion);
    }

    [Fact]
    public async Task El_Comprador_puede_listar_y_el_modelo_recibe_el_resultado_de_la_herramienta()
    {
        var consultor = new ConsultorFalso();
        var modelo = new ModeloFalso(
            _ => Respuestas.PideHerramienta("{\"estado\":\"Pendiente\"}", nombre: "listar_requisiciones"),
            mensajes => Respuestas.Final("Hay 2 requisiciones."));

        var r = await ArmarAgente(modelo, "Comprador", consultor).ResponderAsync("¿qué requisiciones hay pendientes?");

        Assert.True(r.Ok);
        Assert.Equal("Pendiente", consultor.UltimoEstado);
        var herramienta = modelo.Pedidos[1].Last()!;
        Assert.Equal("tool", herramienta["role"]!.GetValue<string>());
        Assert.Contains("REQ-0001", herramienta["content"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("Comprador", "buscar_insumos")]
    [InlineData("Comprador", "armar_borrador_requisicion")]
    [InlineData("Comprador", "listar_ordenes_pendientes")]
    [InlineData("Comprador", "preparar_recepcion")]
    [InlineData("Capataz", "preparar_aprobacion_requisicion")]
    [InlineData("Capataz", "preparar_rechazo_requisicion")]
    [InlineData("Capataz", "preparar_publicacion_licitacion")]
    [InlineData("Capataz", "listar_licitaciones")]
    [InlineData("Capataz", "ver_licitacion")]
    [InlineData("Capataz", "listar_cotizaciones")]
    [InlineData("Capataz", "preparar_adjudicacion")]
    public async Task Si_el_modelo_pide_una_herramienta_que_no_es_de_su_perfil_no_se_ejecuta(string rol, string herramienta)
    {
        var consultor = new ConsultorFalso();
        var modelo = new ModeloFalso(
            _ => Respuestas.PideHerramienta(
                "{\"texto\":\"cemento\",\"requisicionId\":\"" + ConsultorFalso.IdPendiente + "\"}", nombre: herramienta),
            _ => Respuestas.Final("No puedo."));

        var r = await ArmarAgente(modelo, rol, consultor).ResponderAsync("hola");

        Assert.True(r.Ok);
        Assert.Equal(0, consultor.LlamadasLista);
        Assert.Equal(0, consultor.LlamadasDetalle);
        Assert.Contains("herramienta_no_permitida", modelo.Pedidos[1].Last()!["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task Una_herramienta_que_no_existe_sigue_devolviendo_herramienta_desconocida()
    {
        var modelo = new ModeloFalso(
            _ => Respuestas.PideHerramienta("{}", nombre: "borrar_todo"),
            _ => Respuestas.Final("No puedo."));

        await ArmarAgente(modelo, "Admin").ResponderAsync("hola");

        Assert.Contains("herramienta_desconocida", modelo.Pedidos[1].Last()!["content"]!.GetValue<string>());
    }

    [Fact]
    public void Ninguna_herramienta_de_ningun_perfil_escribe_datos()
    {
        // Regla de oro (#146): el modelo nunca escribe. Hoy ningún perfil tiene una herramienta que cree, apruebe o rechace.
        var todas = new[] { "Capataz", "Comprador", "Admin" }
            .SelectMany(r => PerfilAsistente.Para(r)!.Herramientas)
            .Distinct()
            .ToList();

        // preparar_aprobacion_requisicion solo ARMA un borrador: la escritura la hace el frontend al Confirmar.
        Assert.DoesNotContain(todas, n => n.StartsWith("aprobar") || n.Contains("rechazar") || n.StartsWith("crear") || n.Contains("adjudicar"));
    }

    [Fact]
    public async Task El_Comprador_pide_aprobar_y_la_respuesta_trae_el_borrador_de_aprobacion()
    {
        var consultor = new ConsultorFalso();
        var modelo = new ModeloFalso(
            _ => Respuestas.PideHerramienta(
                "{\"requisicionId\":\"" + ConsultorFalso.IdPendiente + "\"}", nombre: "preparar_aprobacion_requisicion"),
            _ => Respuestas.Final("Revisá el resumen y tocá Confirmar."));

        var r = await ArmarAgente(modelo, "Comprador", consultor).ResponderAsync("aprobá la REQ-0001");

        Assert.True(r.Ok);
        Assert.NotNull(r.Aprobacion);
        Assert.Equal(ConsultorFalso.IdPendiente, r.Aprobacion!.RequisicionId);
        Assert.Null(r.Borrador);
        // Nada se escribió: el consultor solo tiene operaciones de lectura.
        Assert.Equal(1, consultor.LlamadasDetalle);
    }

    [Fact]
    public async Task El_Capataz_pide_registrar_lo_que_llego_y_la_respuesta_trae_el_borrador_de_recepcion()
    {
        var recepciones = new ConsultorRecepcionesFalso();
        var modelo = new ModeloFalso(
            _ => Respuestas.PideHerramienta(
                "{\"ordenCompraId\":\"" + ConsultorRecepcionesFalso.IdOrden + "\",\"numeroRemito\":\"R-0001-55\"}", nombre: "preparar_recepcion"),
            _ => Respuestas.Final("Revisá el resumen y tocá Confirmar."));

        var r = await ArmarAgente(modelo, "Capataz", null, recepciones).ResponderAsync("llegó el cemento, remito R-0001-55");

        Assert.True(r.Ok);
        Assert.NotNull(r.Recepcion);
        Assert.Equal("R-0001-55", r.Recepcion!.Recepcion.NumeroRemito);
        Assert.Null(r.Borrador);
        Assert.Null(r.Aprobacion);
        // El modelo solo recibe el resumen corto, no el borrador completo.
        Assert.DoesNotContain("detalles", modelo.Pedidos[1].Last()!["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task El_Comprador_pide_rechazar_y_la_respuesta_trae_el_borrador_con_el_motivo()
    {
        var consultor = new ConsultorFalso();
        var modelo = new ModeloFalso(
            _ => Respuestas.PideHerramienta(
                "{\"requisicionId\":\"" + ConsultorFalso.IdPendiente + "\",\"motivo\":\"Faltan las cantidades\"}",
                nombre: "preparar_rechazo_requisicion"),
            _ => Respuestas.Final("Revisá el resumen y tocá Confirmar."));

        var r = await ArmarAgente(modelo, "Comprador", consultor).ResponderAsync("rechazá la REQ-0001, faltan las cantidades");

        Assert.True(r.Ok);
        Assert.NotNull(r.Aprobacion);
        Assert.Equal("rechazarRequisicion", r.Aprobacion!.Accion);
        Assert.Equal("Faltan las cantidades", r.Aprobacion.Motivo);
        Assert.Equal(ConsultorFalso.IdPendiente, r.Aprobacion.RequisicionId);
        Assert.Null(r.Borrador);
        // Nada se escribió: el consultor solo tiene operaciones de lectura.
        Assert.Equal(1, consultor.LlamadasDetalle);
    }

    [Fact]
    public async Task Si_el_Comprador_no_dio_motivo_el_modelo_recibe_motivo_requerido_y_no_hay_borrador()
    {
        var consultor = new ConsultorFalso();
        var modelo = new ModeloFalso(
            _ => Respuestas.PideHerramienta(
                "{\"requisicionId\":\"" + ConsultorFalso.IdPendiente + "\"}", nombre: "preparar_rechazo_requisicion"),
            _ => Respuestas.Final("¿Cuál es el motivo del rechazo?"));

        var r = await ArmarAgente(modelo, "Comprador", consultor).ResponderAsync("rechazá la REQ-0001");

        Assert.True(r.Ok);
        Assert.Null(r.Aprobacion);
        Assert.Equal(0, consultor.LlamadasDetalle);
        Assert.Contains("motivo_requerido", modelo.Pedidos[1].Last()!["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task El_Comprador_pide_publicar_y_la_respuesta_trae_el_borrador_con_los_dias()
    {
        var consultor = new ConsultorFalso();
        consultor.ResultadoDetalle = ResultadoRequisicion.Bien(new RequisicionDetalle(
            ConsultorFalso.Pendiente with { Estado = "Aprobada" }, null, null,
            new[] { new ItemRequisicion("Arena gruesa", 10, "M3") }));
        var modelo = new ModeloFalso(
            _ => Respuestas.PideHerramienta(
                "{\"requisicionId\":\"" + ConsultorFalso.IdPendiente + "\",\"diasParaCotizar\":5}",
                nombre: "preparar_publicacion_licitacion"),
            _ => Respuestas.Final("Revisá el resumen y tocá Publicar."));

        var r = await ArmarAgente(modelo, "Comprador", consultor).ResponderAsync("publicá la licitación de la REQ-0001, 5 días");

        Assert.True(r.Ok);
        Assert.NotNull(r.Aprobacion);
        Assert.Equal("publicarLicitacion", r.Aprobacion!.Accion);
        Assert.Equal(5, r.Aprobacion.DiasParaCotizar);
        Assert.Null(r.Borrador);
        // Nada se escribió: el consultor solo tiene operaciones de lectura.
        Assert.Equal(1, consultor.LlamadasDetalle);
    }

    [Fact]
    public async Task El_Comprador_compara_ofertas_con_dos_herramientas_y_el_modelo_recibe_las_cifras_del_backend()
    {
        var licitaciones = new ConsultorLicitacionesFalso();
        var modelo = new ModeloFalso(
            _ => Respuestas.PideHerramienta("{\"estado\":\"Abierta\"}", nombre: "listar_licitaciones"),
            _ => Respuestas.PideHerramienta(
                "{\"licitacionId\":\"" + ConsultorLicitacionesFalso.IdLicitacion + "\"}", nombre: "listar_cotizaciones"),
            _ => Respuestas.Final("La mejor es Ferretería Sur, con 82.5 puntos."));

        var r = await ArmarAgente(modelo, "Comprador", licitaciones: licitaciones)
            .ResponderAsync("comparame las ofertas de la licitación del galpón");

        // Alcanza con 2 herramientas y 3 vueltas al modelo: el límite del arnés no se toca.
        Assert.True(r.Ok);
        Assert.Equal(2, r.Herramientas);
        Assert.Equal(1, licitaciones.LlamadasLista);
        Assert.Equal(1, licitaciones.LlamadasOfertas);
        Assert.Equal("Abierta", licitaciones.UltimoEstado);
        var deLaTool = modelo.Pedidos[2].Last()!["content"]!.GetValue<string>();
        Assert.Contains("82.5", deLaTool);
        Assert.Contains("95000", deLaTool);
        // Es solo lectura: no hay ningún borrador.
        Assert.Null(r.Aprobacion);
        Assert.Null(r.Borrador);
        Assert.Null(r.Recepcion);
    }

    [Fact]
    public async Task El_Comprador_pregunta_por_una_licitacion_que_no_existe_y_el_modelo_recibe_el_error()
    {
        var licitaciones = new ConsultorLicitacionesFalso
        {
            ResultadoOfertas = ResultadoCotizaciones.Falla("licitacion_no_encontrada")
        };
        var modelo = new ModeloFalso(
            _ => Respuestas.PideHerramienta(
                "{\"licitacionId\":\"" + Guid.NewGuid() + "\"}", nombre: "listar_cotizaciones"),
            _ => Respuestas.Final("No encontré esa licitación."));

        var r = await ArmarAgente(modelo, "Comprador", licitaciones: licitaciones).ResponderAsync("comparame las ofertas");

        Assert.True(r.Ok);
        Assert.Contains("licitacion_no_encontrada", modelo.Pedidos[1].Last()!["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task Un_Capataz_que_pide_comparar_ofertas_recibe_herramienta_no_permitida_y_no_se_consulta_nada()
    {
        var licitaciones = new ConsultorLicitacionesFalso();
        var modelo = new ModeloFalso(
            _ => Respuestas.PideHerramienta(
                "{\"licitacionId\":\"" + ConsultorLicitacionesFalso.IdLicitacion + "\"}", nombre: "listar_cotizaciones"),
            _ => Respuestas.Final("No puedo."));

        var r = await ArmarAgente(modelo, "Capataz", licitaciones: licitaciones).ResponderAsync("comparame las ofertas del hierro");

        Assert.True(r.Ok);
        Assert.Equal(0, licitaciones.LlamadasOfertas);
        Assert.Contains("herramienta_no_permitida", modelo.Pedidos[1].Last()!["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task Las_instrucciones_de_Compras_piden_copiar_las_cifras_y_no_inventar_datos_del_proveedor()
    {
        var modelo = new ModeloQueAnotaHerramientas(Respuestas.Final("Hola"));

        await ArmarAgente(modelo, "Comprador").ResponderAsync("hola");

        var sistema = modelo.Sistema[0];
        Assert.Contains("listar_cotizaciones", sistema);
        Assert.Contains("nunca hagas cuentas", sistema);
        Assert.Contains("no inventes reputación", sistema);
    }

    [Fact]
    public async Task El_Comprador_adjudica_en_un_mismo_pedido_con_tres_herramientas_y_la_respuesta_trae_el_borrador()
    {
        var licitaciones = new ConsultorLicitacionesFalso();
        var modelo = new ModeloFalso(
            _ => Respuestas.PideHerramienta("{\"estado\":\"Abierta\"}", nombre: "listar_licitaciones"),
            _ => Respuestas.PideHerramienta(
                "{\"licitacionId\":\"" + ConsultorLicitacionesFalso.IdLicitacion + "\"}", nombre: "listar_cotizaciones"),
            _ => Respuestas.PideHerramienta(
                "{\"licitacionId\":\"" + ConsultorLicitacionesFalso.IdLicitacion + "\",\"cotizacionId\":\"" +
                ConsultorLicitacionesFalso.IdCotizacionA + "\"}", nombre: "preparar_adjudicacion"),
            _ => Respuestas.Final("Revisá el resumen: adjudicar no se puede deshacer. Si está bien, tocá Confirmar."));

        var r = await ArmarAgente(modelo, "Comprador", licitaciones: licitaciones)
            .ResponderAsync("adjudicá la oferta de Ferretería Sur de la licitación del galpón");

        Assert.True(r.Ok);
        Assert.Equal(3, r.Herramientas);
        Assert.NotNull(r.Adjudicacion);
        Assert.Equal("adjudicarCotizacion", r.Adjudicacion!.Accion);
        Assert.Equal(ConsultorLicitacionesFalso.IdCotizacionA, r.Adjudicacion.CotizacionId);
        Assert.Null(r.Aprobacion);
        Assert.Null(r.Borrador);
        // Nada se escribió: el consultor solo tiene operaciones de lectura.
        Assert.Equal(2, licitaciones.LlamadasOfertas);
        Assert.Equal(1, licitaciones.LlamadasDetalle);
    }

    [Fact]
    public async Task Si_el_modelo_no_arma_texto_despues_de_preparar_la_adjudicacion_se_pone_el_aviso_de_siempre()
    {
        var modelo = new ModeloFalso(
            _ => Respuestas.PideHerramienta(
                "{\"licitacionId\":\"" + ConsultorLicitacionesFalso.IdLicitacion + "\",\"cotizacionId\":\"" +
                ConsultorLicitacionesFalso.IdCotizacionA + "\"}", nombre: "preparar_adjudicacion"),
            _ => Respuestas.Final(""));

        var r = await ArmarAgente(modelo, "Comprador").ResponderAsync("adjudicá la de Ferretería Sur");

        Assert.True(r.Ok);
        Assert.NotNull(r.Adjudicacion);
        Assert.Contains("Confirmar", r.Texto);
    }

    [Fact]
    public async Task Un_Capataz_no_puede_preparar_una_adjudicacion()
    {
        var licitaciones = new ConsultorLicitacionesFalso();
        var modelo = new ModeloFalso(
            _ => Respuestas.PideHerramienta(
                "{\"licitacionId\":\"" + ConsultorLicitacionesFalso.IdLicitacion + "\",\"cotizacionId\":\"" +
                ConsultorLicitacionesFalso.IdCotizacionA + "\"}", nombre: "preparar_adjudicacion"),
            _ => Respuestas.Final("No puedo."));

        var r = await ArmarAgente(modelo, "Capataz", licitaciones: licitaciones).ResponderAsync("adjudicá la oferta");

        Assert.True(r.Ok);
        Assert.Null(r.Adjudicacion);
        Assert.Equal(0, licitaciones.LlamadasDetalle);
        Assert.Contains("herramienta_no_permitida", modelo.Pedidos[1].Last()!["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task Las_instrucciones_de_Compras_dicen_que_adjudicar_es_irreversible_y_que_elige_la_persona()
    {
        var modelo = new ModeloQueAnotaHerramientas(Respuestas.Final("Hola"));

        await ArmarAgente(modelo, "Comprador").ResponderAsync("hola");

        var sistema = modelo.Sistema[0];
        Assert.Contains("preparar_adjudicacion", sistema);
        Assert.Contains("NO SE PUEDE DESHACER", sistema);
        Assert.Contains("nunca elijas vos", sistema);
    }
}
