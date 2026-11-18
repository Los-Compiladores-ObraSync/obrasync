using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ObraSync.Asistente.Application;

namespace ObraSync.Asistente.Tests;

/// <summary>
/// Tarjeta #98: el servidor MCP (POST /api/mcp, Streamable HTTP) expone las mismas herramientas de solo lectura.
/// Se prueba con JSON-RPC "a mano" (initialize + el método pedido), sin modelo de IA ni Azure.
/// </summary>
public class McpEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Ruta = "/api/mcp";
    private const string RutaPresupuestos = "/api/mcp/presupuestos";

    private readonly WebApplicationFactory<Program> _fabrica;

    public McpEndpointTests(WebApplicationFactory<Program> fabrica)
    {
        _fabrica = fabrica.WithWebHostBuilder(b => b.UseSetting("Jwt:Secret", AsistenteEndpointTests.Secreto));
    }

    private HttpClient Cliente(
        IBuscadorInsumos? buscador = null,
        IBuscadorObras? obras = null,
        IConsultorPresupuestos? presupuestos = null,
        IConsultorOrdenesBloqueadas? ordenesBloqueadas = null,
        bool sinContextoHttp = false) =>
        _fabrica.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<IBuscadorInsumos>();
            s.AddScoped(_ => buscador ?? new BuscadorFalso());
            s.RemoveAll<IBuscadorObras>();
            s.AddScoped(_ => obras ?? new ObrasFalsas());
            s.RemoveAll<IConsultorPresupuestos>();
            s.AddScoped(_ => presupuestos ?? new ConsultorPresupuestosFalso());
            s.RemoveAll<IConsultorOrdenesBloqueadas>();
            s.AddScoped(_ => ordenesBloqueadas ?? new ConsultorOrdenesBloqueadasFalso());
            if (sinContextoHttp)
            {
                s.RemoveAll<IHttpContextAccessor>();
                s.AddSingleton<IHttpContextAccessor, AccesorHttpNulo>();
            }
        })).CreateClient();

    private static HttpRequestMessage Rpc(string? token, string metodo, object? parametros, int id, string ruta = Ruta)
    {
        var cuerpo = new { jsonrpc = "2.0", id, method = metodo, @params = parametros };
        var pedido = new HttpRequestMessage(HttpMethod.Post, ruta) { Content = JsonContent.Create(cuerpo) };
        // Streamable HTTP: el cliente tiene que aceptar JSON y eventos (SSE).
        pedido.Headers.Accept.ParseAdd("application/json");
        pedido.Headers.Accept.ParseAdd("text/event-stream");
        if (token is not null)
            pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return pedido;
    }

    /// <summary>El servidor puede responder JSON directo o un evento SSE ("data: {...}"): se lee cualquiera de los dos.</summary>
    private static async Task<JsonElement> LeerRespuestaAsync(HttpResponseMessage respuesta)
    {
        var texto = (await respuesta.Content.ReadAsStringAsync()).Trim();
        if (!texto.StartsWith('{'))
        {
            texto = texto.Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.StartsWith("data:"))
                .Select(l => l["data:".Length..].Trim())
                .First(l => l.Contains("\"jsonrpc\""));
        }
        return JsonDocument.Parse(texto).RootElement.Clone();
    }

    private static async Task InicializarAsync(HttpClient cliente, string token, string ruta)
    {
        var r = await cliente.SendAsync(Rpc(token, "initialize", new
        {
            protocolVersion = "2025-06-18",
            capabilities = new { },
            clientInfo = new { name = "pruebas-obrasync", version = "1.0.0" }
        }, id: 1, ruta));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    private static async Task<JsonElement> LlamarAsync(
        HttpClient cliente,
        string token,
        string metodo,
        object? parametros,
        string ruta = Ruta)
    {
        await InicializarAsync(cliente, token, ruta);
        var r = await cliente.SendAsync(Rpc(token, metodo, parametros, id: 2, ruta));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await LeerRespuestaAsync(r);
    }

    private sealed class ConsultorPresupuestosFalso : IConsultorPresupuestos
    {
        private static readonly Guid Obra = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        public int Llamadas { get; private set; }
        public Guid? UltimaObraId { get; private set; }
        public string? UltimaEtapa { get; private set; }

        public Task<ResultadoPartidasPresupuestarias> ListarPartidasAsync(Guid obraId, string? etapa, CancellationToken ct)
        {
            Llamadas++;
            UltimaObraId = obraId;
            UltimaEtapa = etapa;
            return Task.FromResult(ResultadoPartidasPresupuestarias.Bien(new[]
            {
                new PartidaPresupuestariaAsistente(
                    Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"), obraId, etapa,
                    "EST-01", "Hormigón", 100m, 25m, 10m, 75m, "ARS")
            }, 1));
        }
    }

    private sealed class ConsultorOrdenesBloqueadasFalso : IConsultorOrdenesBloqueadas
    {
        public int Llamadas { get; private set; }

        public Task<ResultadoOrdenesBloqueadas> ListarAsync(Guid obraId, int pagina, int tamano, CancellationToken ct)
        {
            Llamadas++;
            return Task.FromResult(ResultadoOrdenesBloqueadas.Bien(new[]
            {
                new OrdenCompraBloqueadaAsistente(
                    Guid.Parse("cccccccc-0000-0000-0000-000000000001"), obraId, "Estructura", 120m,
                    100m, 25m, 145m, 45m, "exceso de presupuesto",
                    Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"),
                    new DateTime(2026, 11, 16, 10, 30, 0, DateTimeKind.Utc))
            }, 1));
        }
    }

    private sealed class AccesorHttpNulo : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => null; set { } }
    }

    [Fact]
    public async Task Sin_token_devuelve_401()
    {
        var r = await Cliente().SendAsync(Rpc(null, "tools/list", new { }, id: 1));

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Theory]
    [InlineData("Comprador")]
    [InlineData("Proveedor")]
    public async Task Un_rol_sin_acceso_al_asistente_recibe_403(string rol)
    {
        var r = await Cliente().SendAsync(Rpc(AsistenteEndpointTests.CrearToken(rol), "tools/list", new { }, id: 1));

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Theory]
    [InlineData("Capataz")]
    [InlineData("Admin")]
    public async Task Capataz_y_Admin_ven_las_herramientas_originales_en_la_ruta_original(string rol)
    {
        var respuesta = await LlamarAsync(Cliente(), AsistenteEndpointTests.CrearToken(rol), "tools/list", new { });

        var herramientas = respuesta.GetProperty("result").GetProperty("tools").EnumerateArray().ToList();
        var nombres = herramientas.Select(h => h.GetProperty("name").GetString()).OrderBy(n => n).ToArray();
        // Si alguien agrega una herramienta, esta prueba obliga a revisar que NO escriba datos.
        Assert.Equal(new[] { "armar_borrador_requisicion", "buscar_insumos" }, nombres);
        Assert.All(herramientas, h =>
            Assert.True(h.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean()));
    }

    [Theory]
    [InlineData("Comprador")]
    [InlineData("Admin")]
    public async Task Ruta_presupuestos_muestra_y_ejecuta_solo_la_herramienta_de_auditoria(string rol)
    {
        var consultor = new ConsultorPresupuestosFalso();
        var consultorOrdenes = new ConsultorOrdenesBloqueadasFalso();
        var cliente = Cliente(presupuestos: consultor, ordenesBloqueadas: consultorOrdenes);
        var token = AsistenteEndpointTests.CrearToken(rol);

        var listado = await LlamarAsync(cliente, token, "tools/list", new { }, RutaPresupuestos);
        var herramientas = listado.GetProperty("result").GetProperty("tools").EnumerateArray().ToList();
        var herramienta = Assert.Single(herramientas);
        Assert.Equal("consultar_partidas_presupuestarias", herramienta.GetProperty("name").GetString());
        Assert.True(herramienta.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());

        var llamada = await LlamarAsync(cliente, token, "tools/call", new
        {
            name = "consultar_partidas_presupuestarias",
            arguments = new { obraId = "aaaaaaaa-0000-0000-0000-000000000001", etapa = "Estructura" }
        }, RutaPresupuestos);
        var texto = llamada.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
        var datos = JsonDocument.Parse(texto).RootElement;

        Assert.Equal(1, datos.GetProperty("ordenesBloqueadas").GetArrayLength());
        Assert.Equal("exceso de presupuesto", datos.GetProperty("ordenesBloqueadas")[0].GetProperty("motivo").GetString());
        Assert.Equal(45m, datos.GetProperty("ordenesBloqueadas")[0].GetProperty("exceso").GetDecimal());
        Assert.True(datos.GetProperty("completo").GetBoolean());

        Assert.Equal("Hormigón", datos.GetProperty("partidas")[0].GetProperty("descripcion").GetString());
        Assert.Equal(1, consultor.Llamadas);
        Assert.Equal(1, consultorOrdenes.Llamadas);
        Assert.Equal(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), consultor.UltimaObraId);
        Assert.Equal("Estructura", consultor.UltimaEtapa);
    }

    [Fact]
    public async Task Capataz_recibe_403_en_la_ruta_presupuestaria_por_permisos_del_endpoint_de_ordenes()
    {
        var respuesta = await Cliente().SendAsync(Rpc(
            AsistenteEndpointTests.CrearToken("Capataz"), "tools/list", new { }, id: 1, ruta: RutaPresupuestos));

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Theory]
    [InlineData("Capataz")]
    [InlineData("Admin")]
    public async Task Ruta_original_rechaza_ejecutar_la_herramienta_presupuestaria(string rol)
    {
        var respuesta = await LlamarAsync(Cliente(), AsistenteEndpointTests.CrearToken(rol), "tools/call", new
        {
            name = "consultar_partidas_presupuestarias",
            arguments = new { obraId = "aaaaaaaa-0000-0000-0000-000000000001", etapa = "Estructura" }
        });

        Assert.True(respuesta.GetProperty("result").GetProperty("isError").GetBoolean());
    }

    [Theory]
    [InlineData("buscar_insumos", "{\"texto\":\"cemento\"}")]
    [InlineData("armar_borrador_requisicion", "{\"obra\":\"Torre Central\",\"items\":[{\"insumo\":\"cemento\",\"cantidad\":1}]}")]
    public async Task Ruta_presupuestos_rechaza_las_herramientas_originales(string nombre, string argumentos)
    {
        var respuesta = await LlamarAsync(Cliente(), AsistenteEndpointTests.CrearToken("Comprador"), "tools/call",
            new { name = nombre, arguments = JsonDocument.Parse(argumentos).RootElement.Clone() }, RutaPresupuestos);

        Assert.True(respuesta.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Contains("no está permitida", respuesta.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Contexto_HTTP_nulo_no_lista_herramientas_ni_permite_llamadas()
    {
        var cliente = Cliente(sinContextoHttp: true);
        var token = AsistenteEndpointTests.CrearToken("Comprador");

        var listado = await LlamarAsync(cliente, token, "tools/list", new { }, RutaPresupuestos);
        Assert.Empty(listado.GetProperty("result").GetProperty("tools").EnumerateArray());

        var llamada = await LlamarAsync(cliente, token, "tools/call", new
        {
            name = "buscar_insumos",
            arguments = new { texto = "cemento" }
        }, RutaPresupuestos);
        Assert.True(llamada.GetProperty("result").GetProperty("isError").GetBoolean());
    }

    [Fact]
    public async Task Ruta_MCP_desconocida_no_devuelve_herramientas_ni_ejecuta_llamadas()
    {
        var cliente = Cliente();
        var token = AsistenteEndpointTests.CrearToken("Comprador");

        var listado = await cliente.SendAsync(Rpc(token, "tools/list", new { }, 1, "/api/mcp/no-existe"));
        Assert.Equal(HttpStatusCode.NotFound, listado.StatusCode);
        var llamada = await cliente.SendAsync(Rpc(token, "tools/call", new
        {
            name = "buscar_insumos",
            arguments = new { texto = "cemento" }
        }, 2, "/api/mcp/no-existe"));
        Assert.Equal(HttpStatusCode.NotFound, llamada.StatusCode);
    }

    [Fact]
    public async Task Buscar_insumos_por_MCP_devuelve_el_catalogo()
    {
        var respuesta = await LlamarAsync(Cliente(), AsistenteEndpointTests.CrearToken("Capataz"), "tools/call",
            new { name = "buscar_insumos", arguments = new { texto = "cemento" } });

        var texto = respuesta.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();
        Assert.Contains("MAT-CEM-001", texto);
    }

    [Fact]
    public async Task Armar_borrador_por_MCP_resuelve_obra_etapa_e_insumos_y_no_expone_ids()
    {
        var respuesta = await LlamarAsync(Cliente(), AsistenteEndpointTests.CrearToken("Capataz"), "tools/call", new
        {
            name = "armar_borrador_requisicion",
            arguments = new
            {
                obra = "Torre Central",
                etapa = "Estructura",
                items = new[] { new { insumo = "cemento", cantidad = 10 } }
            }
        });

        var texto = respuesta.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("borradorArmado", texto);
        Assert.Contains("Cemento Portland 50 kg", texto);
        Assert.DoesNotContain("00000000-0000-0000-0000-000000000001", texto);
    }

    [Fact]
    public async Task Armar_borrador_por_MCP_con_un_insumo_inexistente_explica_el_error_y_no_arma_nada()
    {
        var respuesta = await LlamarAsync(Cliente(), AsistenteEndpointTests.CrearToken("Capataz"), "tools/call", new
        {
            name = "armar_borrador_requisicion",
            arguments = new
            {
                obra = "Torre Central",
                etapa = "Estructura",
                items = new[] { new { insumo = "ladrillo de oro", cantidad = 3 } }
            }
        });

        var texto = respuesta.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("insumo_no_encontrado", texto);
        Assert.DoesNotContain("borradorArmado", texto);
    }
}
