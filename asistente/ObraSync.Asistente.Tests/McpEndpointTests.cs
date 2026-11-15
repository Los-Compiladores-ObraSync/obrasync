using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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

    private readonly WebApplicationFactory<Program> _fabrica;

    public McpEndpointTests(WebApplicationFactory<Program> fabrica)
    {
        _fabrica = fabrica.WithWebHostBuilder(b => b.UseSetting("Jwt:Secret", AsistenteEndpointTests.Secreto));
    }

    private HttpClient Cliente(IBuscadorInsumos? buscador = null, IBuscadorObras? obras = null) =>
        _fabrica.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<IBuscadorInsumos>();
            s.AddScoped(_ => buscador ?? new BuscadorFalso());
            s.RemoveAll<IBuscadorObras>();
            s.AddScoped(_ => obras ?? new ObrasFalsas());
        })).CreateClient();

    private static HttpRequestMessage Rpc(string? token, string metodo, object? parametros, int id)
    {
        var cuerpo = new { jsonrpc = "2.0", id, method = metodo, @params = parametros };
        var pedido = new HttpRequestMessage(HttpMethod.Post, Ruta) { Content = JsonContent.Create(cuerpo) };
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

    private static async Task InicializarAsync(HttpClient cliente, string token)
    {
        var r = await cliente.SendAsync(Rpc(token, "initialize", new
        {
            protocolVersion = "2025-06-18",
            capabilities = new { },
            clientInfo = new { name = "pruebas-obrasync", version = "1.0.0" }
        }, id: 1));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    private static async Task<JsonElement> LlamarAsync(HttpClient cliente, string token, string metodo, object? parametros)
    {
        await InicializarAsync(cliente, token);
        var r = await cliente.SendAsync(Rpc(token, metodo, parametros, id: 2));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await LeerRespuestaAsync(r);
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
    public async Task Capataz_y_Admin_ven_exactamente_las_dos_herramientas_de_solo_lectura(string rol)
    {
        var respuesta = await LlamarAsync(Cliente(), AsistenteEndpointTests.CrearToken(rol), "tools/list", new { });

        var herramientas = respuesta.GetProperty("result").GetProperty("tools").EnumerateArray().ToList();
        var nombres = herramientas.Select(h => h.GetProperty("name").GetString()).OrderBy(n => n).ToArray();
        // Si alguien agrega una herramienta, esta prueba obliga a revisar que NO escriba datos.
        Assert.Equal(new[] { "armar_borrador_requisicion", "buscar_insumos" }, nombres);
        Assert.All(herramientas, h =>
            Assert.True(h.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean()));
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
