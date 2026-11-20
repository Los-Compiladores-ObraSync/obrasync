using System.Text.Json.Nodes;
using ObraSync.Asistente.Application;

namespace ObraSync.Asistente.Tests;

/// <summary>Herramientas del perfil Comprador (#141): solo lectura, con argumentos validados antes de llamar a la API.</summary>
public class HerramientasComprasTests
{
    private static (HerramientasCompras Herramientas, ConsultorFalso Consultor, ObrasFalsas Obras) Armar()
    {
        var consultor = new ConsultorFalso();
        var obras = new ObrasFalsas();
        return (new HerramientasCompras(consultor, obras), consultor, obras);
    }

    private static JsonObject Datos(ResultadoHerramienta r) => (JsonObject)r.Datos;

    [Fact]
    public void Las_definiciones_son_las_seis_y_el_estado_es_una_lista_cerrada()
    {
        var defs = HerramientasCompras.Definiciones();

        var nombres = defs.Select(d => d!["function"]!["name"]!.GetValue<string>()).ToList();
        Assert.Equal(
            new[]
            {
                "listar_requisiciones", "ver_requisicion", "preparar_aprobacion_requisicion",
                "preparar_rechazo_requisicion", "preparar_publicacion_licitacion", "consultar_presupuesto_obra"
            },
            nombres);
        var estados = defs[0]!["function"]!["parameters"]!["properties"]!["estado"]!["enum"]!.AsArray()
            .Select(n => n!.GetValue<string>());
        Assert.Equal(HerramientasCompras.Estados, estados);
    }

    // ── Presupuesto de una obra (consultar_presupuesto_obra) ────────────────────────────────────────

    private sealed class PartidasFalsas : IConsultorPresupuestos
    {
        public Guid? UltimaObraId { get; private set; }
        public string? UltimaEtapa { get; private set; }

        public Task<ResultadoPartidasPresupuestarias> ListarPartidasAsync(Guid obraId, string? etapa, CancellationToken ct)
        {
            UltimaObraId = obraId;
            UltimaEtapa = etapa;
            var partida = new PartidaPresupuestariaAsistente(
                Guid.NewGuid(), obraId, "Estructura", "EST-01", "Hormigón", 100000m, 25000m, 10000m, 75000m, "ARS");
            return Task.FromResult(ResultadoPartidasPresupuestarias.Bien(new[] { partida }, 1));
        }
    }

    private sealed class SinBloqueadas : IConsultorOrdenesBloqueadas
    {
        public Task<ResultadoOrdenesBloqueadas> ListarAsync(Guid obraId, int pagina, int tamano, CancellationToken ct) =>
            Task.FromResult(ResultadoOrdenesBloqueadas.Bien(Array.Empty<OrdenCompraBloqueadaAsistente>(), 0));
    }

    private static (HerramientasCompras Herramientas, PartidasFalsas Partidas) ArmarConPresupuesto()
    {
        var partidas = new PartidasFalsas();
        var presupuestos = new HerramientasPresupuestos(partidas, new SinBloqueadas());
        return (new HerramientasCompras(new ConsultorFalso(), new ObrasFalsas(), presupuestos), partidas);
    }

    [Fact]
    public async Task Presupuesto_resuelve_la_obra_por_nombre_y_consulta_sus_partidas()
    {
        var (h, partidas) = ArmarConPresupuesto();

        var r = await h.EjecutarAsync(
            HerramientasCompras.ConsultarPresupuesto, "{\"obra\":\"torre\",\"etapa\":\"Estructura\"}", default);

        Assert.True(r.Ok);
        Assert.Equal(ObrasFalsas.IdTorre, partidas.UltimaObraId);
        Assert.Equal("Estructura", partidas.UltimaEtapa);
    }

    [Fact]
    public async Task Presupuesto_con_obra_que_no_existe_devuelve_las_opciones_y_no_consulta()
    {
        var (h, partidas) = ArmarConPresupuesto();

        var r = await h.EjecutarAsync(HerramientasCompras.ConsultarPresupuesto, "{\"obra\":\"Edificio Fantasma\"}", default);

        Assert.False(r.Ok);
        Assert.Equal("obra_no_encontrada", Datos(r)["error"]!.GetValue<string>());
        Assert.NotEmpty(Datos(r)["obras"]!.AsArray());
        Assert.Null(partidas.UltimaObraId);
    }

    [Fact]
    public async Task Presupuesto_sin_nombre_de_obra_pide_elegir_una()
    {
        var (h, partidas) = ArmarConPresupuesto();

        var r = await h.EjecutarAsync(HerramientasCompras.ConsultarPresupuesto, "{}", default);

        Assert.False(r.Ok);
        Assert.Equal("obra_requerida", Datos(r)["error"]!.GetValue<string>());
        Assert.Null(partidas.UltimaObraId);
    }

    [Fact]
    public async Task Presupuesto_sin_la_herramienta_de_presupuestos_responde_no_disponible()
    {
        var (h, _, _) = Armar();

        var r = await h.EjecutarAsync(HerramientasCompras.ConsultarPresupuesto, "{\"obra\":\"Torre\"}", default);

        Assert.False(r.Ok);
        Assert.Equal("herramienta_no_disponible", Datos(r)["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task Listar_devuelve_las_requisiciones_con_el_nombre_de_la_obra_y_el_total()
    {
        var (h, consultor, _) = Armar();

        var r = await h.EjecutarAsync(HerramientasCompras.ListarRequisiciones, "{}", default);

        Assert.True(r.Ok);
        var datos = Datos(r);
        Assert.Equal(2, datos["total"]!.GetValue<int>());
        var lista = datos["requisiciones"]!.AsArray();
        Assert.Equal(2, lista.Count);
        // Se mostraron todas: el modelo no tiene nada que avisar.
        Assert.Equal(2, datos["mostradas"]!.GetValue<int>());
        Assert.False(datos["hayMas"]!.GetValue<bool>());
        Assert.Equal("REQ-0001", lista[0]!["numero"]!.GetValue<string>());
        Assert.Equal("Pendiente", lista[0]!["estado"]!.GetValue<string>());
        Assert.Equal("Torre Central", lista[0]!["obra"]!.GetValue<string>());
        Assert.Equal("Estructura", lista[0]!["etapa"]!.GetValue<string>());
        Assert.Equal("2026-11-10", lista[0]!["fecha"]!.GetValue<string>());
        Assert.Equal(2, lista[0]!["cantidadItems"]!.GetValue<int>());
        Assert.Equal(ConsultorFalso.IdPendiente.ToString(), lista[0]!["requisicionId"]!.GetValue<string>());
        // Se piden ya acotadas, sin filtro de estado.
        Assert.Equal(HerramientasCompras.MaxRequisiciones, consultor.UltimoMaximo);
        Assert.Null(consultor.UltimoEstado);
    }

    [Theory]
    [InlineData("pendiente", "Pendiente")]
    [InlineData("APROBADA", "Aprobada")]
    [InlineData(" Rechazada ", "Rechazada")]
    public async Task Listar_filtra_por_estado_sin_distinguir_mayusculas(string pedido, string esperado)
    {
        var (h, consultor, _) = Armar();

        var r = await h.EjecutarAsync(HerramientasCompras.ListarRequisiciones, $"{{\"estado\":\"{pedido}\"}}", default);

        Assert.True(r.Ok);
        Assert.Equal(esperado, consultor.UltimoEstado);
    }

    [Theory]
    [InlineData("{\"estado\":\"\"}")]
    [InlineData("{\"estado\":null}")]
    [InlineData("")]
    public async Task Listar_sin_estado_trae_todas(string argumentos)
    {
        var (h, consultor, _) = Armar();

        var r = await h.EjecutarAsync(HerramientasCompras.ListarRequisiciones, argumentos, default);

        Assert.True(r.Ok);
        Assert.Null(consultor.UltimoEstado);
        Assert.Equal(1, consultor.LlamadasLista);
    }

    [Theory]
    [InlineData("{\"estado\":\"Cancelada\"}")]
    [InlineData("{\"estado\":5}")]
    [InlineData("{\"estado\":[\"Pendiente\"]}")]
    [InlineData("no es json")]
    [InlineData("[1,2]")]
    public async Task Listar_con_argumentos_invalidos_no_llama_a_la_API(string argumentos)
    {
        var (h, consultor, obras) = Armar();

        var r = await h.EjecutarAsync(HerramientasCompras.ListarRequisiciones, argumentos, default);

        Assert.False(r.Ok);
        Assert.Equal("argumentos_invalidos", Datos(r)["error"]!.GetValue<string>());
        Assert.Equal(0, consultor.LlamadasLista);
        Assert.Equal(0, obras.Llamadas);
    }

    [Fact]
    public async Task Listar_nunca_pasa_de_cinco_aunque_la_API_devuelva_mas()
    {
        var (h, consultor, _) = Armar();
        var muchas = Enumerable.Range(1, 9)
            .Select(i => new RequisicionResumen(Guid.NewGuid(), $"REQ-{i:0000}", "Pendiente", ObrasFalsas.IdTorre, null, DateTime.UtcNow, 1))
            .ToList();
        consultor.ResultadoLista = ResultadoListaRequisiciones.Bien(muchas, 9);

        var r = await h.EjecutarAsync(HerramientasCompras.ListarRequisiciones, "{}", default);

        var datos = Datos(r);
        Assert.Equal(5, datos["requisiciones"]!.AsArray().Count);
        // El total real se informa igual: así el modelo puede decir "hay 9 pendientes, te muestro las 5 últimas".
        Assert.Equal(9, datos["total"]!.GetValue<int>());
        // Y se lo decimos con un dato explícito, para que avise que hay más de las que muestra.
        Assert.Equal(5, datos["mostradas"]!.GetValue<int>());
        Assert.True(datos["hayMas"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Si_las_obras_no_se_pueden_leer_la_lista_igual_se_devuelve_sin_nombre_de_obra()
    {
        var (h, _, obras) = Armar();
        obras.Resultado = ResultadoObras.Falla("api_no_disponible");

        var r = await h.EjecutarAsync(HerramientasCompras.ListarRequisiciones, "{}", default);

        Assert.True(r.Ok);
        Assert.Null(Datos(r)["requisiciones"]![0]!["obra"]);
    }

    [Fact]
    public async Task Un_error_de_la_API_se_devuelve_como_codigo_estable()
    {
        var (h, consultor, _) = Armar();
        consultor.ResultadoLista = ResultadoListaRequisiciones.Falla("sin_permiso");

        var r = await h.EjecutarAsync(HerramientasCompras.ListarRequisiciones, "{}", default);

        Assert.False(r.Ok);
        Assert.Equal("sin_permiso", Datos(r)["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task Ver_devuelve_el_detalle_con_los_materiales()
    {
        var (h, consultor, _) = Armar();
        var args = $"{{\"requisicionId\":\"{ConsultorFalso.IdPendiente}\"}}";

        var r = await h.EjecutarAsync(HerramientasCompras.VerRequisicion, args, default);

        Assert.True(r.Ok);
        var datos = Datos(r);
        Assert.Equal("REQ-0001", datos["numero"]!.GetValue<string>());
        Assert.Equal("Torre Central", datos["obra"]!.GetValue<string>());
        Assert.Equal("Urge para el colado", datos["observaciones"]!.GetValue<string>());
        Assert.Null(datos["motivoRechazo"]);
        var items = datos["items"]!.AsArray();
        Assert.Equal(2, items.Count);
        Assert.Equal("Cemento Portland 50 kg", items[0]!["descripcion"]!.GetValue<string>());
        Assert.Equal(20m, items[0]!["cantidad"]!.GetValue<decimal>());
        Assert.Equal("BOLSA", items[0]!["unidadMedida"]!.GetValue<string>());
        Assert.Equal(ConsultorFalso.IdPendiente, consultor.UltimoId);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"requisicionId\":\"\"}")]
    [InlineData("{\"requisicionId\":\"REQ-0001\"}")]
    [InlineData("{\"requisicionId\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData("{\"requisicionId\":12}")]
    [InlineData("no es json")]
    public async Task Ver_con_un_id_invalido_no_llama_a_la_API(string argumentos)
    {
        var (h, consultor, _) = Armar();

        var r = await h.EjecutarAsync(HerramientasCompras.VerRequisicion, argumentos, default);

        Assert.False(r.Ok);
        Assert.Equal("argumentos_invalidos", Datos(r)["error"]!.GetValue<string>());
        Assert.Equal(0, consultor.LlamadasDetalle);
    }

    [Fact]
    public async Task Ver_una_requisicion_que_no_existe_devuelve_un_error_estable()
    {
        var (h, consultor, _) = Armar();
        consultor.ResultadoDetalle = ResultadoRequisicion.Falla("requisicion_no_encontrada");

        var r = await h.EjecutarAsync(
            HerramientasCompras.VerRequisicion, $"{{\"requisicionId\":\"{Guid.NewGuid()}\"}}", default);

        Assert.False(r.Ok);
        Assert.Equal("requisicion_no_encontrada", Datos(r)["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task Los_textos_libres_largos_se_acotan_antes_de_pasarselos_al_modelo()
    {
        var (h, consultor, _) = Armar();
        var largo = new string('x', 5000);
        consultor.ResultadoDetalle = ResultadoRequisicion.Bien(new RequisicionDetalle(
            ConsultorFalso.Pendiente, largo, largo, new[] { new ItemRequisicion(largo, 1, "UNIDAD") }));

        var r = await h.EjecutarAsync(
            HerramientasCompras.VerRequisicion, $"{{\"requisicionId\":\"{ConsultorFalso.IdPendiente}\"}}", default);

        var datos = Datos(r);
        Assert.Equal(HerramientasCompras.MaxTextoLibre, datos["observaciones"]!.GetValue<string>().Length);
        Assert.Equal(HerramientasCompras.MaxTextoLibre, datos["motivoRechazo"]!.GetValue<string>().Length);
        Assert.Equal(HerramientasCompras.MaxTextoLibre, datos["items"]![0]!["descripcion"]!.GetValue<string>().Length);
    }

    [Fact]
    public async Task Una_herramienta_desconocida_devuelve_herramienta_desconocida()
    {
        var (h, _, _) = Armar();

        var r = await h.EjecutarAsync("aprobar_requisicion", "{}", default);

        Assert.False(r.Ok);
        Assert.Equal("herramienta_desconocida", Datos(r)["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task Preparar_aprobacion_de_una_pendiente_arma_el_borrador_sin_aprobar_nada()
    {
        var (h, consultor, _) = Armar();
        var args = $"{{\"requisicionId\":\"{ConsultorFalso.IdPendiente}\"}}";

        var r = await h.EjecutarAsync(HerramientasCompras.PrepararAprobacion, args, default);

        Assert.True(r.Ok);
        var b = r.Aprobacion!;
        Assert.Equal("aprobarRequisicion", b.Accion);
        Assert.Equal("Aprobar requisición REQ-0001", b.Titulo);
        Assert.Equal(ConsultorFalso.IdPendiente, b.RequisicionId);
        Assert.Equal("Torre Central", b.Resumen.Obra);
        Assert.Equal("Estructura", b.Resumen.Etapa);
        Assert.Equal(2, b.Resumen.Items.Count);
        Assert.Equal("Cemento Portland 50 kg", b.Resumen.Items[0].Descripcion);
        // La requisición se vuelve a leer para confirmar el estado; el modelo solo ve un resumen corto.
        Assert.Equal(1, consultor.LlamadasDetalle);
        Assert.True(Datos(r)["borradorListo"]!.GetValue<bool>());
        Assert.Null(r.Borrador);
    }

    [Theory]
    [InlineData("Aprobada")]
    [InlineData("Rechazada")]
    public async Task Preparar_aprobacion_de_una_que_no_esta_pendiente_no_arma_borrador(string estado)
    {
        var (h, consultor, _) = Armar();
        var yaResuelta = ConsultorFalso.Pendiente with { Estado = estado };
        consultor.ResultadoDetalle = ResultadoRequisicion.Bien(
            new RequisicionDetalle(yaResuelta, null, null, new[] { new ItemRequisicion("Arena", 1, "M3") }));

        var r = await h.EjecutarAsync(
            HerramientasCompras.PrepararAprobacion, $"{{\"requisicionId\":\"{ConsultorFalso.IdPendiente}\"}}", default);

        Assert.False(r.Ok);
        Assert.Null(r.Aprobacion);
        Assert.Equal("requisicion_no_pendiente", Datos(r)["error"]!.GetValue<string>());
        Assert.Equal(estado, Datos(r)["estado"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"requisicionId\":\"REQ-0001\"}")]
    [InlineData("{\"requisicionId\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData("no es json")]
    public async Task Preparar_aprobacion_con_un_id_invalido_no_llama_a_la_API(string argumentos)
    {
        var (h, consultor, _) = Armar();

        var r = await h.EjecutarAsync(HerramientasCompras.PrepararAprobacion, argumentos, default);

        Assert.False(r.Ok);
        Assert.Null(r.Aprobacion);
        Assert.Equal(0, consultor.LlamadasDetalle);
    }

    [Fact]
    public async Task Preparar_aprobacion_de_una_que_no_existe_devuelve_error_estable()
    {
        var (h, consultor, _) = Armar();
        consultor.ResultadoDetalle = ResultadoRequisicion.Falla("requisicion_no_encontrada");

        var r = await h.EjecutarAsync(
            HerramientasCompras.PrepararAprobacion, $"{{\"requisicionId\":\"{Guid.NewGuid()}\"}}", default);

        Assert.False(r.Ok);
        Assert.Null(r.Aprobacion);
        Assert.Equal("requisicion_no_encontrada", Datos(r)["error"]!.GetValue<string>());
    }

    // ---- Rechazar una requisición (#149): igual que aprobar, solo ARMA el borrador y lleva el motivo.

    private static string ArgsRechazo(Guid id, string motivo) =>
        new JsonObject { ["requisicionId"] = id.ToString(), ["motivo"] = motivo }.ToJsonString();

    [Fact]
    public async Task Preparar_rechazo_de_una_pendiente_arma_el_borrador_con_el_motivo_sin_rechazar_nada()
    {
        var (h, consultor, _) = Armar();

        var r = await h.EjecutarAsync(
            HerramientasCompras.PrepararRechazo, ArgsRechazo(ConsultorFalso.IdPendiente, "  Falta el presupuesto de la etapa.  "), default);

        Assert.True(r.Ok);
        var b = r.Aprobacion!;
        Assert.Equal("rechazarRequisicion", b.Accion);
        Assert.Equal("Rechazar requisición REQ-0001", b.Titulo);
        Assert.Equal(ConsultorFalso.IdPendiente, b.RequisicionId);
        // El motivo viaja sin los espacios de los bordes: es lo que se va a mandar al confirmar.
        Assert.Equal("Falta el presupuesto de la etapa.", b.Motivo);
        Assert.Equal("Torre Central", b.Resumen.Obra);
        Assert.Equal(2, b.Resumen.Items.Count);
        // La requisición se vuelve a leer para confirmar el estado.
        Assert.Equal(1, consultor.LlamadasDetalle);
        Assert.True(Datos(r)["borradorListo"]!.GetValue<bool>());
    }

    [Fact]
    public async Task El_borrador_de_aprobacion_sigue_sin_motivo()
    {
        var (h, _, _) = Armar();

        var r = await h.EjecutarAsync(
            HerramientasCompras.PrepararAprobacion, $"{{\"requisicionId\":\"{ConsultorFalso.IdPendiente}\"}}", default);

        Assert.Equal("aprobarRequisicion", r.Aprobacion!.Accion);
        Assert.Null(r.Aprobacion.Motivo);
    }

    [Theory]
    [InlineData("{\"requisicionId\":\"{ID}\"}")]
    [InlineData("{\"requisicionId\":\"{ID}\",\"motivo\":null}")]
    [InlineData("{\"requisicionId\":\"{ID}\",\"motivo\":\"\"}")]
    [InlineData("{\"requisicionId\":\"{ID}\",\"motivo\":\"    \"}")]
    [InlineData("{\"requisicionId\":\"{ID}\",\"motivo\":\"\\n\\t\"}")]
    public async Task Preparar_rechazo_sin_motivo_no_llama_a_la_API(string argumentos)
    {
        var (h, consultor, _) = Armar();

        var r = await h.EjecutarAsync(
            HerramientasCompras.PrepararRechazo, argumentos.Replace("{ID}", ConsultorFalso.IdPendiente.ToString()), default);

        Assert.False(r.Ok);
        Assert.Null(r.Aprobacion);
        Assert.Equal("motivo_requerido", Datos(r)["error"]!.GetValue<string>());
        Assert.Equal(0, consultor.LlamadasDetalle);
    }

    [Fact]
    public async Task Preparar_rechazo_con_un_motivo_de_mas_de_500_caracteres_no_llama_a_la_API()
    {
        var (h, consultor, _) = Armar();

        var r = await h.EjecutarAsync(
            HerramientasCompras.PrepararRechazo, ArgsRechazo(ConsultorFalso.IdPendiente, new string('a', HerramientasCompras.MaxMotivo + 1)), default);

        Assert.False(r.Ok);
        Assert.Null(r.Aprobacion);
        Assert.Equal("motivo_demasiado_largo", Datos(r)["error"]!.GetValue<string>());
        Assert.Equal(0, consultor.LlamadasDetalle);
    }

    [Fact]
    public async Task Preparar_rechazo_acepta_un_motivo_de_exactamente_500_caracteres()
    {
        var (h, _, _) = Armar();
        var motivo = new string('a', HerramientasCompras.MaxMotivo);

        var r = await h.EjecutarAsync(HerramientasCompras.PrepararRechazo, ArgsRechazo(ConsultorFalso.IdPendiente, motivo), default);

        Assert.True(r.Ok);
        Assert.Equal(motivo, r.Aprobacion!.Motivo);
    }

    [Fact]
    public async Task Preparar_rechazo_cambia_los_caracteres_de_control_del_motivo_por_espacios()
    {
        var (h, _, _) = Armar();

        var r = await h.EjecutarAsync(
            HerramientasCompras.PrepararRechazo, ArgsRechazo(ConsultorFalso.IdPendiente, "Precio\nalto\u0007 por bolsa"), default);

        Assert.True(r.Ok);
        Assert.Equal("Precio alto  por bolsa", r.Aprobacion!.Motivo);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"requisicionId\":\"REQ-0001\",\"motivo\":\"No\"}")]
    [InlineData("{\"requisicionId\":\"00000000-0000-0000-0000-000000000000\",\"motivo\":\"No\"}")]
    [InlineData("no es json")]
    public async Task Preparar_rechazo_con_un_id_invalido_no_llama_a_la_API(string argumentos)
    {
        var (h, consultor, _) = Armar();

        var r = await h.EjecutarAsync(HerramientasCompras.PrepararRechazo, argumentos, default);

        Assert.False(r.Ok);
        Assert.Null(r.Aprobacion);
        Assert.Equal(0, consultor.LlamadasDetalle);
    }

    [Theory]
    [InlineData("Aprobada")]
    [InlineData("Rechazada")]
    public async Task Preparar_rechazo_de_una_que_no_esta_pendiente_no_arma_borrador(string estado)
    {
        var (h, consultor, _) = Armar();
        var yaResuelta = ConsultorFalso.Pendiente with { Estado = estado };
        consultor.ResultadoDetalle = ResultadoRequisicion.Bien(
            new RequisicionDetalle(yaResuelta, null, null, new[] { new ItemRequisicion("Arena", 1, "M3") }));

        var r = await h.EjecutarAsync(
            HerramientasCompras.PrepararRechazo, ArgsRechazo(ConsultorFalso.IdPendiente, "No corresponde"), default);

        Assert.False(r.Ok);
        Assert.Null(r.Aprobacion);
        Assert.Equal("requisicion_no_pendiente", Datos(r)["error"]!.GetValue<string>());
        Assert.Equal(estado, Datos(r)["estado"]!.GetValue<string>());
    }

    [Fact]
    public async Task Preparar_rechazo_de_una_que_no_existe_devuelve_error_estable()
    {
        var (h, consultor, _) = Armar();
        consultor.ResultadoDetalle = ResultadoRequisicion.Falla("requisicion_no_encontrada");

        var r = await h.EjecutarAsync(
            HerramientasCompras.PrepararRechazo, ArgsRechazo(Guid.NewGuid(), "No corresponde"), default);

        Assert.False(r.Ok);
        Assert.Null(r.Aprobacion);
        Assert.Equal("requisicion_no_encontrada", Datos(r)["error"]!.GetValue<string>());
    }

    // ---- Publicar la licitación de una requisición Aprobada (#149): solo ARMA el borrador, con los días de plazo.

    private static string ArgsPublicar(Guid id, object? dias) =>
        new JsonObject { ["requisicionId"] = id.ToString(), ["diasParaCotizar"] = JsonValue.Create(dias) }.ToJsonString();

    private static void DejarAprobada(ConsultorFalso consultor, string estado = "Aprobada") =>
        consultor.ResultadoDetalle = ResultadoRequisicion.Bien(new RequisicionDetalle(
            ConsultorFalso.Pendiente with { Estado = estado }, null, null,
            new[] { new ItemRequisicion("Cemento Portland 50 kg", 20, "BOLSA") }));

    [Fact]
    public async Task Preparar_publicacion_de_una_aprobada_arma_el_borrador_con_los_dias_sin_publicar_nada()
    {
        var (h, consultor, _) = Armar();
        DejarAprobada(consultor);

        var r = await h.EjecutarAsync(
            HerramientasCompras.PrepararPublicacion, ArgsPublicar(ConsultorFalso.IdPendiente, 7), default);

        Assert.True(r.Ok);
        var b = r.Aprobacion!;
        Assert.Equal("publicarLicitacion", b.Accion);
        Assert.Equal("Publicar licitación de la requisición REQ-0001", b.Titulo);
        Assert.Equal(ConsultorFalso.IdPendiente, b.RequisicionId);
        Assert.Equal(7, b.DiasParaCotizar);
        Assert.Null(b.Motivo);
        Assert.Equal("Torre Central", b.Resumen.Obra);
        Assert.Single(b.Resumen.Items);
        // La requisición se vuelve a leer para confirmar el estado; el modelo solo ve un resumen corto.
        Assert.Equal(1, consultor.LlamadasDetalle);
        Assert.True(Datos(r)["borradorListo"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    public async Task Preparar_publicacion_acepta_los_extremos_del_plazo(int dias)
    {
        var (h, consultor, _) = Armar();
        DejarAprobada(consultor);

        var r = await h.EjecutarAsync(
            HerramientasCompras.PrepararPublicacion, ArgsPublicar(ConsultorFalso.IdPendiente, dias), default);

        Assert.True(r.Ok);
        Assert.Equal(dias, r.Aprobacion!.DiasParaCotizar);
    }

    [Fact]
    public async Task Preparar_publicacion_acepta_un_entero_escrito_como_7_punto_0()
    {
        var (h, consultor, _) = Armar();
        DejarAprobada(consultor);
        var args = $"{{\"requisicionId\":\"{ConsultorFalso.IdPendiente}\",\"diasParaCotizar\":7.0}}";

        var r = await h.EjecutarAsync(HerramientasCompras.PrepararPublicacion, args, default);

        Assert.True(r.Ok);
        Assert.Equal(7, r.Aprobacion!.DiasParaCotizar);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("31")]
    [InlineData("2.5")]
    [InlineData("\"7\"")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("99999999999")]
    public async Task Preparar_publicacion_con_un_plazo_invalido_no_llama_a_la_API(string dias)
    {
        var (h, consultor, _) = Armar();
        var args = $"{{\"requisicionId\":\"{ConsultorFalso.IdPendiente}\",\"diasParaCotizar\":{dias}}}";

        var r = await h.EjecutarAsync(HerramientasCompras.PrepararPublicacion, args, default);

        Assert.False(r.Ok);
        Assert.Null(r.Aprobacion);
        Assert.Equal("plazo_invalido", Datos(r)["error"]!.GetValue<string>());
        Assert.Equal(0, consultor.LlamadasDetalle);
    }

    [Fact]
    public async Task Preparar_publicacion_sin_plazo_no_llama_a_la_API()
    {
        var (h, consultor, _) = Armar();
        var args = $"{{\"requisicionId\":\"{ConsultorFalso.IdPendiente}\"}}";

        var r = await h.EjecutarAsync(HerramientasCompras.PrepararPublicacion, args, default);

        Assert.False(r.Ok);
        Assert.Equal("plazo_invalido", Datos(r)["error"]!.GetValue<string>());
        Assert.Equal(0, consultor.LlamadasDetalle);
    }

    [Theory]
    [InlineData("Pendiente")]
    [InlineData("Rechazada")]
    public async Task Preparar_publicacion_de_una_que_no_esta_aprobada_no_arma_borrador(string estado)
    {
        var (h, consultor, _) = Armar();
        DejarAprobada(consultor, estado);

        var r = await h.EjecutarAsync(
            HerramientasCompras.PrepararPublicacion, ArgsPublicar(ConsultorFalso.IdPendiente, 7), default);

        Assert.False(r.Ok);
        Assert.Null(r.Aprobacion);
        Assert.Equal("requisicion_no_aprobada", Datos(r)["error"]!.GetValue<string>());
        Assert.Equal(estado, Datos(r)["estado"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"requisicionId\":\"REQ-0001\",\"diasParaCotizar\":7}")]
    [InlineData("{\"requisicionId\":\"00000000-0000-0000-0000-000000000000\",\"diasParaCotizar\":7}")]
    [InlineData("no es json")]
    public async Task Preparar_publicacion_con_un_id_invalido_no_llama_a_la_API(string argumentos)
    {
        var (h, consultor, _) = Armar();

        var r = await h.EjecutarAsync(HerramientasCompras.PrepararPublicacion, argumentos, default);

        Assert.False(r.Ok);
        Assert.Null(r.Aprobacion);
        Assert.Equal(0, consultor.LlamadasDetalle);
    }

    [Fact]
    public async Task Preparar_publicacion_de_una_que_no_existe_devuelve_error_estable()
    {
        var (h, consultor, _) = Armar();
        consultor.ResultadoDetalle = ResultadoRequisicion.Falla("requisicion_no_encontrada");

        var r = await h.EjecutarAsync(
            HerramientasCompras.PrepararPublicacion, ArgsPublicar(Guid.NewGuid(), 7), default);

        Assert.False(r.Ok);
        Assert.Null(r.Aprobacion);
        Assert.Equal("requisicion_no_encontrada", Datos(r)["error"]!.GetValue<string>());
    }
}
