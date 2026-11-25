using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using NSubstitute;
using ObraSync.Asistente.Application;
using ObraSync.Asistente.Infrastructure;

namespace ObraSync.Asistente.Tests;

/// <summary>
/// Tarjeta #100: el asistente ARMA un borrador de requisición, pero nunca crea nada.
/// Estas pruebas cubren las reglas de la herramienta, que el modelo no tiene herramientas de escritura
/// y que todo lo que se consulta al backend es GET con el JWT del usuario.
/// </summary>
public class BorradorRequisicionTests
{
    private const string IdCemento = "00000000-0000-0000-0000-000000000001";
    private const string IdArena = "00000000-0000-0000-0000-000000000002";

    private static JsonNode N(double valor) => JsonValue.Create(valor)!;

    /// <summary>Arma el JSON de argumentos como lo mandaría el modelo.</summary>
    private static string Args(string? obra, string? etapa, params (string Insumo, JsonNode? Cantidad)[] items)
    {
        var o = new JsonObject();
        if (obra is not null) o["obra"] = obra;
        if (etapa is not null) o["etapa"] = etapa;
        var arr = new JsonArray();
        foreach (var (insumo, cantidad) in items)
            arr.Add(new JsonObject { ["insumo"] = insumo, ["cantidad"] = cantidad });
        o["items"] = arr;
        return o.ToJsonString();
    }

    private static Task<ResultadoHerramienta> Armar(
        string argumentos, IBuscadorInsumos? buscador = null, IBuscadorObras? obras = null) =>
        new HerramientasCatalogo(buscador ?? new BuscadorFalso(), obras ?? new ObrasFalsas())
            .EjecutarAsync(HerramientasCatalogo.ArmarBorrador, argumentos, default);

    // ---------- Camino feliz ----------

    [Fact]
    public async Task Borrador_valido_resuelve_obra_etapa_e_insumos_reales()
    {
        var r = await Armar(Args("torre central", "estructura",
            ("cemento", N(10)), ("arena", N(2.5))));

        Assert.True(r.Ok);
        var b = r.Borrador!;
        Assert.Equal("crearRequisicion", b.Accion);
        Assert.Equal(ObrasFalsas.IdTorre, b.Requisicion.ObraId);
        Assert.Equal("Estructura", b.Requisicion.Etapa);
        Assert.Equal(2, b.Requisicion.Items.Count);
        Assert.Equal(Guid.Parse(IdCemento), b.Requisicion.Items[0].ArticuloId);
        Assert.Equal(10, b.Requisicion.Items[0].Cantidad);
        Assert.Equal(Guid.Parse(IdArena), b.Requisicion.Items[1].ArticuloId);
        Assert.Equal(2.5, b.Requisicion.Items[1].Cantidad);

        // El resumen de pantalla usa los nombres del catálogo, no lo que escribió el modelo.
        Assert.Equal("Torre Central", b.Resumen.Obra);
        Assert.Equal("Cemento Portland 25 kg", b.Resumen.Items[0].Descripcion);
        Assert.Equal("BOLSA", b.Resumen.Items[0].UnidadMedida);
    }

    [Fact]
    public async Task El_modelo_ve_el_resumen_pero_no_los_ids()
    {
        var r = await Armar(Args("Torre Central", "Estructura", ("cemento", N(10))));

        var visto = r.Datos.ToJsonString();
        Assert.Contains("Cemento Portland 25 kg", visto);
        Assert.DoesNotContain(IdCemento, visto);
        Assert.DoesNotContain(ObrasFalsas.IdTorre.ToString(), visto);
    }

    [Fact]
    public async Task La_cantidad_como_texto_numerico_se_acepta()
    {
        var r = await Armar(Args("Torre Central", "Estructura", ("cemento", JsonValue.Create("10"))));

        Assert.True(r.Ok);
        Assert.Equal(10, r.Borrador!.Requisicion.Items[0].Cantidad);
    }

    [Fact]
    public async Task Una_obra_sin_etapas_arma_el_borrador_con_etapa_nula()
    {
        var r = await Armar(Args("Galpón Norte", null, ("hierro", N(5))));

        Assert.True(r.Ok);
        Assert.Null(r.Borrador!.Requisicion.Etapa);
        Assert.Equal(ObrasFalsas.IdGalpon, r.Borrador.Requisicion.ObraId);
    }

    [Fact]
    public async Task Las_observaciones_se_incluyen_sin_espacios_de_mas()
    {
        var args = JsonNode.Parse(Args("Torre Central", "Estructura", ("cemento", N(1))))!.AsObject();
        args["observaciones"] = "  Entregar temprano  ";

        var r = await Armar(args.ToJsonString());

        Assert.Equal("Entregar temprano", r.Borrador!.Requisicion.Observaciones);
    }

    // ---------- Reglas de la tarjeta #100 ----------

    [Fact]
    public async Task Insumo_inexistente_no_arma_borrador()
    {
        var r = await Armar(Args("Torre Central", "Estructura", ("ladrillo", N(3))));

        Assert.False(r.Ok);
        Assert.Null(r.Borrador);
        var texto = r.Datos.ToJsonString();
        Assert.Contains("items_invalidos", texto);
        Assert.Contains("insumo_no_encontrado", texto);
    }

    [Fact]
    public async Task Insumo_ambiguo_devuelve_opciones_y_no_arma_borrador()
    {
        var buscador = Substitute.For<IBuscadorInsumos>();
        buscador.BuscarAsync("cemento", Arg.Any<CancellationToken>()).Returns(ResultadoBusqueda.Bien(new[]
        {
            new Insumo(IdCemento, "MAT-CEM-001", "Cemento Portland 25 kg", "BOLSA"),
            new Insumo(IdArena, "MAT-CEM-002", "Cemento de albañilería 25 kg", "BOLSA")
        }));

        var r = await Armar(Args("Torre Central", "Estructura", ("cemento", N(3))), buscador);

        Assert.False(r.Ok);
        Assert.Null(r.Borrador);
        var texto = r.Datos.ToJsonString();
        Assert.Contains("insumo_ambiguo", texto);
        Assert.Contains("MAT-CEM-002", texto);
    }

    [Fact]
    public async Task Si_el_texto_coincide_exacto_con_uno_no_es_ambiguo()
    {
        var buscador = Substitute.For<IBuscadorInsumos>();
        buscador.BuscarAsync("MAT-CEM-001", Arg.Any<CancellationToken>()).Returns(ResultadoBusqueda.Bien(new[]
        {
            new Insumo(IdCemento, "MAT-CEM-001", "Cemento Portland 25 kg", "BOLSA"),
            new Insumo(IdArena, "MAT-CEM-0011", "Cemento rápido", "BOLSA")
        }));

        var r = await Armar(Args("Torre Central", "Estructura", ("MAT-CEM-001", N(3))), buscador);

        Assert.True(r.Ok);
        Assert.Equal(Guid.Parse(IdCemento), r.Borrador!.Requisicion.Items[0].ArticuloId);
    }

    [Fact]
    public async Task Insumo_repetido_no_arma_borrador()
    {
        var r = await Armar(Args("Torre Central", "Estructura", ("cemento", N(3)), ("Cemento Portland 25 kg", N(2))));

        Assert.False(r.Ok);
        Assert.Contains("insumo_repetido", r.Datos.ToJsonString());
    }

    [Fact]
    public async Task Obra_que_el_usuario_no_ve_no_se_encuentra_y_no_se_consulta_el_catalogo()
    {
        var buscador = Substitute.For<IBuscadorInsumos>();

        var r = await Armar(Args("Edificio Fantasma", "Estructura", ("cemento", N(3))), buscador);

        Assert.False(r.Ok);
        Assert.Null(r.Borrador);
        Assert.Contains("obra_no_encontrada", r.Datos.ToJsonString());
        Assert.Empty(buscador.ReceivedCalls());
    }

    [Fact]
    public async Task Obra_ambigua_devuelve_opciones()
    {
        var obras = new ObrasFalsas
        {
            Resultado = ResultadoObras.Bien(new[]
            {
                new ObraAsistente(Guid.NewGuid(), "Torre Norte", new[] { "Estructura" }),
                new ObraAsistente(Guid.NewGuid(), "Torre Sur", new[] { "Estructura" })
            })
        };

        var r = await Armar(Args("torre", "Estructura", ("cemento", N(3))), obras: obras);

        Assert.False(r.Ok);
        var texto = r.Datos.ToJsonString();
        Assert.Contains("obra_ambigua", texto);
        Assert.Contains("Torre Sur", texto);
    }

    [Fact]
    public async Task Sin_obra_pide_la_obra_y_ofrece_las_del_usuario()
    {
        var r = await Armar(Args(null, "Estructura", ("cemento", N(3))));

        Assert.False(r.Ok);
        var texto = r.Datos.ToJsonString();
        Assert.Contains("obra_requerida", texto);
        Assert.Contains("Torre Central", texto);
    }

    [Fact]
    public async Task Etapa_faltante_o_inexistente_devuelve_las_etapas_de_la_obra()
    {
        var sin = await Armar(Args("Torre Central", null, ("cemento", N(3))));
        var mala = await Armar(Args("Torre Central", "Terminaciones", ("cemento", N(3))));

        Assert.Contains("etapa_requerida", sin.Datos.ToJsonString());
        Assert.Contains("Obra Gruesa", sin.Datos.ToJsonString());
        Assert.Contains("etapa_no_encontrada", mala.Datos.ToJsonString());
        Assert.Null(sin.Borrador);
        Assert.Null(mala.Borrador);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("\"abc\"")]
    [InlineData("null")]
    public async Task Cantidad_invalida_se_rechaza_sin_llamar_a_ninguna_API(string cantidadJson)
    {
        var buscador = Substitute.For<IBuscadorInsumos>();
        var obras = new ObrasFalsas();
        var args = "{\"obra\":\"Torre Central\",\"etapa\":\"Estructura\",\"items\":[{\"insumo\":\"cemento\",\"cantidad\":" + cantidadJson + "}]}";

        var r = await Armar(args, buscador, obras);

        Assert.False(r.Ok);
        Assert.Null(r.Borrador);
        Assert.Contains("cantidad_invalida", r.Datos.ToJsonString());
        Assert.Equal(0, obras.Llamadas);
        Assert.Empty(buscador.ReceivedCalls());
    }

    [Theory]
    [InlineData("esto no es json")]
    [InlineData("{}")]
    [InlineData("{\"obra\":\"Torre Central\",\"items\":[]}")]
    [InlineData("{\"obra\":\"Torre Central\",\"items\":[5]}")]
    [InlineData("{\"obra\":\"Torre Central\",\"items\":\"cemento\"}")]
    public async Task Argumentos_invalidos_se_rechazan_sin_llamar_a_ninguna_API(string argumentos)
    {
        var obras = new ObrasFalsas();

        var r = await Armar(argumentos, obras: obras);

        Assert.False(r.Ok);
        Assert.Null(r.Borrador);
        Assert.Contains("argumentos_invalidos", r.Datos.ToJsonString());
        Assert.Equal(0, obras.Llamadas);
    }

    [Fact]
    public async Task Mas_de_diez_items_se_rechaza()
    {
        var items = Enumerable.Range(0, HerramientasCatalogo.MaxItemsBorrador + 1)
            .Select(_ => ("cemento", (JsonNode?)N(1)))
            .ToArray();

        var r = await Armar(Args("Torre Central", "Estructura", items));

        Assert.False(r.Ok);
        Assert.Contains("argumentos_invalidos", r.Datos.ToJsonString());
    }

    [Fact]
    public async Task Rol_sin_permiso_en_la_API_de_obras_no_arma_borrador()
    {
        var obras = new ObrasFalsas { Resultado = ResultadoObras.Falla("sin_permiso") };
        var buscador = Substitute.For<IBuscadorInsumos>();

        var r = await Armar(Args("Torre Central", "Estructura", ("cemento", N(3))), buscador, obras);

        Assert.False(r.Ok);
        Assert.Null(r.Borrador);
        Assert.Contains("sin_permiso", r.Datos.ToJsonString());
        Assert.Empty(buscador.ReceivedCalls());
    }

    [Fact]
    public async Task Rol_sin_permiso_en_el_catalogo_no_arma_borrador()
    {
        var buscador = Substitute.For<IBuscadorInsumos>();
        buscador.BuscarAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ResultadoBusqueda.Falla("sin_permiso"));

        var r = await Armar(Args("Torre Central", "Estructura", ("cemento", N(3))), buscador);

        Assert.False(r.Ok);
        Assert.Null(r.Borrador);
        Assert.Contains("sin_permiso", r.Datos.ToJsonString());
    }

    // ---------- El modelo NO puede escribir ----------

    [Fact]
    public void El_modelo_solo_tiene_herramientas_de_lectura_o_de_borrador()
    {
        var nombres = HerramientasCatalogo.Definiciones()
            .Select(d => d!["function"]!["name"]!.GetValue<string>())
            .ToArray();

        // Si alguien agrega una herramienta, esta prueba obliga a revisar que NO escriba datos.
        Assert.Equal(new[] { "buscar_insumos", "listar_mis_obras", "armar_borrador_requisicion" }, nombres);
    }

    [Theory]
    [InlineData("crear_requisicion")]
    [InlineData("aprobar_requisicion")]
    [InlineData("emitir_orden_compra")]
    public async Task Una_herramienta_de_escritura_pedida_por_el_modelo_no_existe(string nombre)
    {
        var r = await new HerramientasCatalogo(new BuscadorFalso(), new ObrasFalsas())
            .EjecutarAsync(nombre, "{}", default);

        Assert.False(r.Ok);
        Assert.Contains("herramienta_desconocida", r.Datos.ToJsonString());
    }

    [Fact]
    public async Task Todo_lo_que_el_asistente_le_pide_al_backend_es_GET_con_el_JWT_del_usuario()
    {
        var handler = new HandlerRutas(new Dictionary<string, string>
        {
            ["/api/obras"] = "[{\"id\":\"" + ObrasFalsas.IdTorre + "\",\"nombre\":\"Torre Central\",\"activa\":true," +
                             "\"etapas\":[{\"id\":\"" + Guid.NewGuid() + "\",\"nombreEtapa\":\"Estructura\",\"estado\":\"EnCurso\"}]}]",
            ["/api/catalogo/insumos"] = "[{\"articuloId\":\"" + IdCemento + "\",\"codigo\":\"MAT-CEM-001\"," +
                                        "\"descripcion\":\"Cemento Portland 25 kg\",\"unidadMedida\":\"BOLSA\"}]"
        });
        var http = new HttpClient(handler);
        var config = new ConfiguracionApi("http://gateway");
        var contexto = new ContextoFalso();
        var herramientas = new HerramientasCatalogo(
            new BuscadorGateway(http, config, contexto),
            new ObrasGateway(http, config, contexto));

        var r = await herramientas.EjecutarAsync(
            HerramientasCatalogo.ArmarBorrador, Args("Torre Central", "Estructura", ("cemento", N(10))), default);

        Assert.True(r.Ok);
        Assert.Equal(Guid.Parse(IdCemento), r.Borrador!.Requisicion.Items[0].ArticuloId);
        Assert.Equal(2, handler.Pedidos.Count);
        Assert.All(handler.Pedidos, p =>
        {
            Assert.Equal(HttpMethod.Get, p.Metodo);
            Assert.Equal("Bearer JWT_DE_PRUEBA", p.Authorization);
        });
    }

    // ---------- Lector de obras (ObrasGateway) ----------

    private static ObrasGateway Gateway(HttpMessageHandler handler, ContextoFalso? contexto = null) =>
        new(new HttpClient(handler), new ConfiguracionApi("http://gateway"), contexto ?? new ContextoFalso());

    [Fact]
    public async Task ObrasGateway_ignora_obras_dadas_de_baja_y_lee_las_etapas()
    {
        var cuerpo = "[" +
            "{\"id\":\"" + ObrasFalsas.IdTorre + "\",\"nombre\":\"Torre Central\",\"activa\":true,\"etapas\":[{\"nombreEtapa\":\"Estructura\"},{\"nombreEtapa\":\"\"}]}," +
            "{\"id\":\"" + ObrasFalsas.IdGalpon + "\",\"nombre\":\"Galpón viejo\",\"activa\":false,\"etapas\":[]}" +
            "]";

        var r = await Gateway(new HandlerCatalogo(200, cuerpo)).ListarAsync(default);

        Assert.Null(r.Error);
        var obra = Assert.Single(r.Obras!);
        Assert.Equal("Torre Central", obra.Nombre);
        Assert.Equal(new[] { "Estructura" }, obra.Etapas);
    }

    [Fact]
    public async Task ObrasGateway_manda_el_JWT_y_el_correlation_id_del_usuario()
    {
        var handler = new HandlerCatalogo(200, "[]");

        await Gateway(handler).ListarAsync(default);

        Assert.Equal("Bearer JWT_DE_PRUEBA", handler.Authorization);
        Assert.Equal("11111111-2222-3333-4444-555555555555", handler.CorrelationId);
        Assert.StartsWith("http://gateway/api/obras", handler.Url);
    }

    [Fact]
    public async Task ObrasGateway_sin_JWT_no_llama_a_la_API()
    {
        var handler = new HandlerCatalogo(200, "[]");

        var r = await Gateway(handler, new ContextoFalso { Jwt = null }).ListarAsync(default);

        Assert.Equal("sin_permiso", r.Error);
        Assert.Null(handler.Url);
    }

    [Theory]
    [InlineData(401, "sin_permiso")]
    [InlineData(403, "sin_permiso")]
    [InlineData(500, "api_http_error")]
    public async Task ObrasGateway_traduce_los_errores_http_a_codigos_estables(int estado, string esperado)
    {
        var r = await Gateway(new HandlerCatalogo(estado, "{}")).ListarAsync(default);

        Assert.Equal(esperado, r.Error);
    }

    [Fact]
    public async Task ObrasGateway_API_caida_devuelve_codigo_estable()
    {
        var r = await Gateway(new HandlerCatalogo(200, "[]", lanzaError: true)).ListarAsync(default);

        Assert.Equal("api_no_disponible", r.Error);
    }

    [Fact]
    public async Task ObrasGateway_json_invalido_devuelve_codigo_estable()
    {
        var r = await Gateway(new HandlerCatalogo(200, "esto no es json")).ListarAsync(default);

        Assert.Equal("respuesta_invalida", r.Error);
    }

    // ---------- El agente ----------

    [Fact]
    public async Task El_agente_devuelve_el_borrador_que_armo_la_herramienta()
    {
        var modelo = new ModeloFalso(
            _ => Respuestas.PideHerramienta(
                Args("Torre Central", "Estructura", ("cemento", N(10))), nombre: HerramientasCatalogo.ArmarBorrador),
            _ => Respuestas.Final("Armé el borrador. Revisalo y confirmalo."));
        var agente = new Agente(modelo, new HerramientasCatalogo(new BuscadorFalso(), new ObrasFalsas()));

        var r = await agente.ResponderAsync("Necesito 10 bolsas de cemento para la etapa de estructura de la Torre Central");

        Assert.True(r.Ok);
        Assert.NotNull(r.Borrador);
        Assert.Equal(1, r.Herramientas);
        Assert.Equal(ObrasFalsas.IdTorre, r.Borrador!.Requisicion.ObraId);
    }

    [Fact]
    public async Task Si_la_herramienta_falla_el_agente_no_devuelve_borrador()
    {
        var modelo = new ModeloFalso(
            _ => Respuestas.PideHerramienta(
                Args("Torre Central", "Estructura", ("ladrillo", N(10))), nombre: HerramientasCatalogo.ArmarBorrador),
            _ => Respuestas.Final("No encontré ese insumo en el catálogo."));
        var agente = new Agente(modelo, new HerramientasCatalogo(new BuscadorFalso(), new ObrasFalsas()));

        var r = await agente.ResponderAsync("Necesito ladrillos");

        Assert.True(r.Ok);
        Assert.Null(r.Borrador);
    }

    [Fact]
    public async Task Armar_el_borrador_cuenta_como_una_sola_llamada_de_herramienta()
    {
        // buscar los insumos y la obra pasa DENTRO de la herramienta: no gasta el presupuesto de 2 llamadas.
        var modelo = new ModeloFalso(
            _ => Respuestas.PideHerramienta(
                Args("Torre Central", "Estructura", ("cemento", N(1)), ("arena", N(1)), ("hierro", N(1))),
                nombre: HerramientasCatalogo.ArmarBorrador),
            _ => Respuestas.Final("Listo."));
        var agente = new Agente(modelo, new HerramientasCatalogo(new BuscadorFalso(), new ObrasFalsas()));

        var r = await agente.ResponderAsync("Pedido de tres materiales");

        Assert.Equal(1, r.Herramientas);
        Assert.Equal(3, r.Borrador!.Requisicion.Items.Count);
    }

    [Fact]
    public void Las_instrucciones_ya_no_mandan_a_cargar_a_mano_y_aclaran_que_no_se_crea_nada()
    {
        Assert.DoesNotContain("a mano", Agente.Instrucciones);
        Assert.Contains("armar_borrador_requisicion", Agente.Instrucciones);
        Assert.Contains("Confirmar", Agente.Instrucciones);
    }
}
