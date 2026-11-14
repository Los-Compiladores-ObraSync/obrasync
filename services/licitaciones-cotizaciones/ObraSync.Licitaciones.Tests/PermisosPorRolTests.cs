using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace ObraSync.Licitaciones.Tests;

public class PermisosPorRolTests : IClassFixture<LicitacionesApiFactory>
{
    private readonly LicitacionesApiFactory _factory;

    public PermisosPorRolTests(LicitacionesApiFactory factory) => _factory = factory;

    private HttpClient Cliente(string? rol, string? correlationId = null)
    {
        var client = _factory.CreateClient();
        if (correlationId is not null)
            client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
        if (rol is not null)
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", LicitacionesApiFactory.CrearToken(rol));
        return client;
    }

    private static async Task AssertProblemJson(HttpResponseMessage resp, int status, string correlationId)
    {
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal(status, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(correlationId, doc.RootElement.GetProperty("correlationId").GetString());
    }

    // ---- Adjudicar: solo Comprador y Admin ----

    [Fact]
    public async Task Adjudicar_sin_token_responde_401_con_problem_json()
    {
        var cid = Guid.NewGuid().ToString();
        var resp = await Cliente(null, cid).PostAsync($"/api/licitaciones/{Guid.NewGuid()}/adjudicar", null);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        await AssertProblemJson(resp, 401, cid);
    }

    [Theory]
    [InlineData("Capataz")]
    [InlineData("Proveedor")]
    public async Task Adjudicar_con_rol_no_autorizado_responde_403_con_problem_json(string rol)
    {
        var cid = Guid.NewGuid().ToString();
        var resp = await Cliente(rol, cid).PostAsync($"/api/licitaciones/{Guid.NewGuid()}/adjudicar", null);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        await AssertProblemJson(resp, 403, cid);
    }

    [Theory]
    [InlineData("Comprador")]
    [InlineData("Admin")]
    public async Task Adjudicar_con_Comprador_o_Admin_pasa_la_autorizacion(string rol)
    {
        // La licitación no existe: lo que importa es que NO sea 401 ni 403
        var resp = await Cliente(rol).PostAsync($"/api/licitaciones/{Guid.NewGuid()}/adjudicar", null);

        Assert.NotEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ---- POST /api/licitaciones: 401 y 403 con correlationId ----

    [Fact]
    public async Task PostLicitacion_sin_token_responde_401_con_problem_json()
    {
        var cid = Guid.NewGuid().ToString();
        var resp = await Cliente(null, cid).PostAsJsonAsync("/api/licitaciones", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        await AssertProblemJson(resp, 401, cid);
    }

    [Theory]
    [InlineData("Capataz")]
    [InlineData("Proveedor")]
    public async Task PostLicitacion_con_rol_no_autorizado_responde_403_con_problem_json(string rol)
    {
        var cid = Guid.NewGuid().ToString();
        var resp = await Cliente(rol, cid).PostAsJsonAsync("/api/licitaciones", new { });

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        await AssertProblemJson(resp, 403, cid);
    }

    // ---- GET /api/licitaciones/{id}: Proveedor sí, Capataz no ----

    [Fact]
    public async Task GetLicitacionPorId_Proveedor_pasa_la_autorizacion_y_Capataz_recibe_403()
    {
        var id = Guid.NewGuid();

        var proveedor = await Cliente("Proveedor").GetAsync($"/api/licitaciones/{id}");
        Assert.NotEqual(HttpStatusCode.Forbidden, proveedor.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, proveedor.StatusCode);

        var capataz = await Cliente("Capataz").GetAsync($"/api/licitaciones/{id}");
        Assert.Equal(HttpStatusCode.Forbidden, capataz.StatusCode);
    }
}