using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Exceptions;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Domain.Enums;
using ObraSync.Recepcion.Infrastructure.Persistence;
using Xunit;
using RecepcionEntity = ObraSync.Recepcion.Domain.Entities.Recepcion;

namespace ObraSync.Recepcion.Tests;

public class RecepcionesPermisosObraCapatazTests : IClassFixture<RecepcionApiFactory>
{
    private readonly RecepcionApiFactory _factory;

    public RecepcionesPermisosObraCapatazTests(RecepcionApiFactory factory)
    {
        _factory = factory;
    }

    private WebApplicationFactory<Program> CrearFactoryConValidador(IObraAsignacionValidator validador)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IObraAsignacionValidator>();
                services.AddScoped<IObraAsignacionValidator>(_ => validador);
            });
        });
    }

    private async Task<(Guid ordenCompraId, Guid obraId, Guid articuloId)> CrearOrdenCompra(Guid? obraId = null)
    {
        var ordenCompraId = Guid.NewGuid();
        var obraFinalId = obraId ?? Guid.NewGuid();
        var articuloId = Guid.NewGuid();

        var articulo = OrdenCompraItem.Crear(
            articuloId,
            100,
            "UNIDAD",
            10,
            "Ladrillos huecos");

        var ordenCompra = OrdenCompra.Crear(
            ordenCompraId,
            obraFinalId,
            Guid.NewGuid(),
            1000,
            new List<OrdenCompraItem> { articulo });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();

        db.OrdenesCompra.Add(ordenCompra);
        await db.SaveChangesAsync();

        return (ordenCompraId, obraFinalId, articuloId);
    }

    private async Task<RecepcionEntity> CrearRecepcionEnDb(Guid obraId, Guid ordenCompraId, Guid articuloId)
    {
        var recepcion = new RecepcionEntity
        {
            Id = Guid.NewGuid(),
            ObraId = obraId,
            OrdenCompraId = ordenCompraId,
            RecepcionadoPorUsuarioId = Guid.NewGuid(),
            NumeroRemito = $"REM-{Guid.NewGuid():N}",
            FechaRecepcion = DateTime.UtcNow,
            Observaciones = "Observaciones prueba",
            Estado = EstadoRecepcion.Parcial,
            Detalles = new List<DetalleRecepcion>
            {
                new DetalleRecepcion
                {
                    Id = Guid.NewGuid(),
                    ArticuloId = articuloId,
                    DescripcionArticulo = "Ladrillos huecos",
                    CantidadEsperada = 100,
                    CantidadRecibida = 100,
                    UnidadMedida = "UNIDAD",
                    Conforme = true
                }
            }
        };

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();

        db.Recepciones.Add(recepcion);
        await db.SaveChangesAsync();

        return recepcion;
    }

    private static object CrearSolicitudRecepcion(Guid ordenCompraId, Guid obraId, Guid articuloId)
    {
        return new
        {
            obraId,
            ordenCompraId,
            numeroRemito = $"REM-{Guid.NewGuid():N}",
            fechaRecepcion = DateTime.UtcNow,
            observaciones = "Recepción de materiales",
            detalles = new[]
            {
                new
                {
                    articuloId,
                    descripcionArticulo = "Ladrillos huecos",
                    cantidadEsperada = 100m,
                    cantidadRecibida = 100m,
                    unidadMedida = "UNIDAD",
                    conforme = true,
                    observacion = (string?)null
                }
            }
        };
    }

    [Fact]
    public async Task PostRecepcion_CapatazObraAjena_Devuelve403ProblemDetails()
    {
        var obraAsignada = Guid.NewGuid();
        var obraAjena = Guid.NewGuid();
        var (ordenId, _, articuloId) = await CrearOrdenCompra(obraAjena);

        var factory = CrearFactoryConValidador(new StubObraAsignacionValidator(obraAsignada));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken(role: "Capataz"));

        var body = CrearSolicitudRecepcion(ordenId, obraAjena, articuloId);
        var response = await client.PostAsJsonAsync("/api/recepciones", body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(403, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Obra no asignada", doc.RootElement.GetProperty("title").GetString());
        Assert.Contains("La obra no está asignada al capataz autenticado", doc.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task PostRecepcion_CapatazUsuariosCaido_Devuelve503ProblemDetails()
    {
        var (ordenId, obraId, articuloId) = await CrearOrdenCompra();

        var factory = CrearFactoryConValidador(new ValidadorQueNoPuedeVerificar());
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken(role: "Capataz"));

        var body = CrearSolicitudRecepcion(ordenId, obraId, articuloId);
        var response = await client.PostAsJsonAsync("/api/recepciones", body);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(503, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Servicio no disponible", doc.RootElement.GetProperty("title").GetString());
        Assert.Contains("No se pudo verificar que la obra esté asignada al capataz", doc.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task PostRecepcion_CapatazObraAsignada_Devuelve201Created()
    {
        var obraAsignada = Guid.NewGuid();
        var (ordenId, _, articuloId) = await CrearOrdenCompra(obraAsignada);

        var factory = CrearFactoryConValidador(new StubObraAsignacionValidator(obraAsignada));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken(role: "Capataz"));

        var body = CrearSolicitudRecepcion(ordenId, obraAsignada, articuloId);
        var response = await client.PostAsJsonAsync("/api/recepciones", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task PostRecepcion_AdminObraCualquiera_Devuelve201Created()
    {
        var obraAsignadaCapataz = Guid.NewGuid();
        var obraDeAdmin = Guid.NewGuid();
        var (ordenId, _, articuloId) = await CrearOrdenCompra(obraDeAdmin);

        // Validador solo conoce obraAsignadaCapataz; Admin debe ser exento (#189)
        var factory = CrearFactoryConValidador(new StubObraAsignacionValidator(obraAsignadaCapataz));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken(role: "Admin"));

        var body = CrearSolicitudRecepcion(ordenId, obraDeAdmin, articuloId);
        var response = await client.PostAsJsonAsync("/api/recepciones", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task PostRecepcion_DiscrepanciaObraIdConOrdenCompra_Devuelve400ProblemDetails()
    {
        var obraAsignada = Guid.NewGuid();
        var otraObra = Guid.NewGuid();
        // La OC pertenece a otraObra
        var (ordenId, _, articuloId) = await CrearOrdenCompra(otraObra);

        // El Capataz tiene asignada obraAsignada
        var factory = CrearFactoryConValidador(new StubObraAsignacionValidator(obraAsignada, otraObra));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken(role: "Capataz"));

        // Se envía obraAsignada en el cuerpo, pero la OC pertenece a otraObra
        var body = CrearSolicitudRecepcion(ordenId, obraAsignada, articuloId);
        var response = await client.PostAsJsonAsync("/api/recepciones", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Contains("La orden de compra no pertenece a la obra indicada", doc.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task GetRecepciones_CapatazObraAjena_Devuelve403ProblemDetails()
    {
        var obraAsignada = Guid.NewGuid();
        var obraAjena = Guid.NewGuid();

        var factory = CrearFactoryConValidador(new StubObraAsignacionValidator(obraAsignada));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken(role: "Capataz"));

        var response = await client.GetAsync($"/api/recepciones?obraId={obraAjena}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(403, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Obra no asignada", doc.RootElement.GetProperty("title").GetString());
        Assert.Contains("La obra no está asignada al capataz autenticado", doc.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task GetRecepciones_CapatazObraAsignada_Devuelve200Ok()
    {
        var obraAsignada = Guid.NewGuid();

        var factory = CrearFactoryConValidador(new StubObraAsignacionValidator(obraAsignada));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken(role: "Capataz"));

        var response = await client.GetAsync($"/api/recepciones?obraId={obraAsignada}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetRecepciones_CapatazSinFiltroObra_FiltraSoloObrasAsignadas()
    {
        var obra1 = Guid.NewGuid();
        var obra2 = Guid.NewGuid();

        var (oc1, _, art1) = await CrearOrdenCompra(obra1);
        var (oc2, _, art2) = await CrearOrdenCompra(obra2);

        var r1 = await CrearRecepcionEnDb(obra1, oc1, art1);
        var r2 = await CrearRecepcionEnDb(obra2, oc2, art2);

        // Capataz solo tiene asignada obra1
        var factory = CrearFactoryConValidador(new StubObraAsignacionValidator(obra1));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken(role: "Capataz"));

        var response = await client.GetFromJsonAsync<List<RecepcionDto>>("/api/recepciones");

        Assert.NotNull(response);
        Assert.Contains(response, r => r.Id == r1.Id && r.ObraId == obra1);
        Assert.DoesNotContain(response, r => r.Id == r2.Id || r.ObraId == obra2);
    }

    [Fact]
    public async Task GetRecepciones_CapatazUsuariosCaido_Devuelve503ProblemDetails()
    {
        var factory = CrearFactoryConValidador(new ValidadorQueNoPuedeVerificar());
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken(role: "Capataz"));

        var response = await client.GetAsync("/api/recepciones");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(503, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Servicio no disponible", doc.RootElement.GetProperty("title").GetString());
        Assert.Contains("No se pudo verificar las obras asignadas al capataz", doc.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task GetRecepciones_Admin_SinFiltroObra_VeTodasLasObras()
    {
        var obra1 = Guid.NewGuid();
        var obra2 = Guid.NewGuid();

        var (oc1, _, art1) = await CrearOrdenCompra(obra1);
        var (oc2, _, art2) = await CrearOrdenCompra(obra2);

        var r1 = await CrearRecepcionEnDb(obra1, oc1, art1);
        var r2 = await CrearRecepcionEnDb(obra2, oc2, art2);

        // Validador solo tiene obra1, pero el usuario es Admin
        var factory = CrearFactoryConValidador(new StubObraAsignacionValidator(obra1));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken(role: "Admin"));

        var response = await client.GetFromJsonAsync<List<RecepcionDto>>("/api/recepciones");

        Assert.NotNull(response);
        Assert.Contains(response, r => r.Id == r1.Id);
        Assert.Contains(response, r => r.Id == r2.Id);
    }

    [Fact]
    public async Task GetOrdenesPendientes_CapatazObraAjena_Devuelve403ProblemDetails()
    {
        var obraAsignada = Guid.NewGuid();
        var obraAjena = Guid.NewGuid();

        var factory = CrearFactoryConValidador(new StubObraAsignacionValidator(obraAsignada));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken(role: "Capataz"));

        var response = await client.GetAsync($"/api/recepciones/ordenes-pendientes?obraId={obraAjena}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(403, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Obra no asignada", doc.RootElement.GetProperty("title").GetString());
        Assert.Contains("La obra no está asignada al capataz autenticado", doc.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task GetOrdenesPendientes_CapatazObraAsignada_Devuelve200Ok()
    {
        var obraAsignada = Guid.NewGuid();
        await CrearOrdenCompra(obraAsignada);

        var factory = CrearFactoryConValidador(new StubObraAsignacionValidator(obraAsignada));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken(role: "Capataz"));

        var response = await client.GetAsync($"/api/recepciones/ordenes-pendientes?obraId={obraAsignada}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetOrdenesPendientes_CapatazSinFiltroObra_FiltraSoloObrasAsignadas()
    {
        var obra1 = Guid.NewGuid();
        var obra2 = Guid.NewGuid();

        var (oc1, _, _) = await CrearOrdenCompra(obra1);
        var (oc2, _, _) = await CrearOrdenCompra(obra2);

        // Capataz solo tiene asignada obra1
        var factory = CrearFactoryConValidador(new StubObraAsignacionValidator(obra1));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken(role: "Capataz"));

        var response = await client.GetFromJsonAsync<List<OrdenPendienteDto>>("/api/recepciones/ordenes-pendientes");

        Assert.NotNull(response);
        Assert.Contains(response, o => o.OrdenCompraId == oc1 && o.ObraId == obra1);
        Assert.DoesNotContain(response, o => o.OrdenCompraId == oc2 || o.ObraId == obra2);
    }

    [Fact]
    public async Task GetOrdenesPendientes_CapatazUsuariosCaido_Devuelve503ProblemDetails()
    {
        var factory = CrearFactoryConValidador(new ValidadorQueNoPuedeVerificar());
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken(role: "Capataz"));

        var response = await client.GetAsync("/api/recepciones/ordenes-pendientes");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(503, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Servicio no disponible", doc.RootElement.GetProperty("title").GetString());
        Assert.Contains("No se pudo verificar las obras asignadas al capataz", doc.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task GetOrdenesPendientes_AdminYComprador_SinFiltroObra_VenTodasLasObras()
    {
        var obra1 = Guid.NewGuid();
        var obra2 = Guid.NewGuid();

        var (oc1, _, _) = await CrearOrdenCompra(obra1);
        var (oc2, _, _) = await CrearOrdenCompra(obra2);

        var factory = CrearFactoryConValidador(new StubObraAsignacionValidator(obra1));

        // Test con Admin
        var adminClient = factory.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken(role: "Admin"));

        var adminResp = await adminClient.GetFromJsonAsync<List<OrdenPendienteDto>>("/api/recepciones/ordenes-pendientes");
        Assert.NotNull(adminResp);
        Assert.Contains(adminResp, o => o.OrdenCompraId == oc1);
        Assert.Contains(adminResp, o => o.OrdenCompraId == oc2);

        // Test con Comprador
        var compradorClient = factory.CreateClient();
        compradorClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken(role: "Comprador"));

        var compradorResp = await compradorClient.GetFromJsonAsync<List<OrdenPendienteDto>>("/api/recepciones/ordenes-pendientes");
        Assert.NotNull(compradorResp);
        Assert.Contains(compradorResp, o => o.OrdenCompraId == oc1);
        Assert.Contains(compradorResp, o => o.OrdenCompraId == oc2);
    }

    [Fact]
    public async Task GetRecepcionPorId_CapatazObraAjena_Devuelve403ProblemDetails()
    {
        var obraAsignada = Guid.NewGuid();
        var obraAjena = Guid.NewGuid();
        var (oc, _, art) = await CrearOrdenCompra(obraAjena);
        var recepcion = await CrearRecepcionEnDb(obraAjena, oc, art);

        var factory = CrearFactoryConValidador(new StubObraAsignacionValidator(obraAsignada));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken(role: "Capataz"));

        var response = await client.GetAsync($"/api/recepciones/{recepcion.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(403, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Obra no asignada", doc.RootElement.GetProperty("title").GetString());
        Assert.Contains("La obra no está asignada al capataz autenticado", doc.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task GetRecepcionPorId_CapatazObraAsignada_Devuelve200Ok()
    {
        var obraAsignada = Guid.NewGuid();
        var (oc, _, art) = await CrearOrdenCompra(obraAsignada);
        var recepcion = await CrearRecepcionEnDb(obraAsignada, oc, art);

        var factory = CrearFactoryConValidador(new StubObraAsignacionValidator(obraAsignada));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken(role: "Capataz"));

        var response = await client.GetAsync($"/api/recepciones/{recepcion.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetRecepcionPorId_CapatazUsuariosCaido_Devuelve503ProblemDetails()
    {
        var obraId = Guid.NewGuid();
        var (oc, _, art) = await CrearOrdenCompra(obraId);
        var recepcion = await CrearRecepcionEnDb(obraId, oc, art);

        var factory = CrearFactoryConValidador(new ValidadorQueNoPuedeVerificar());
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken(role: "Capataz"));

        var response = await client.GetAsync($"/api/recepciones/{recepcion.Id}");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(503, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Servicio no disponible", doc.RootElement.GetProperty("title").GetString());
        Assert.Contains("No se pudo verificar que la obra de la recepción esté asignada al capataz", doc.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task GetRecepcionPorId_AdminObraCualquiera_Devuelve200Ok()
    {
        var obraAsignada = Guid.NewGuid();
        var obraDeAdmin = Guid.NewGuid();
        var (oc, _, art) = await CrearOrdenCompra(obraDeAdmin);
        var recepcion = await CrearRecepcionEnDb(obraDeAdmin, oc, art);

        var factory = CrearFactoryConValidador(new StubObraAsignacionValidator(obraAsignada));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken(role: "Admin"));

        var response = await client.GetAsync($"/api/recepciones/{recepcion.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed class StubObraAsignacionValidator : IObraAsignacionValidator
    {
        private readonly HashSet<Guid> _obrasAsignadas;

        public StubObraAsignacionValidator(params Guid[] obrasAsignadas)
        {
            _obrasAsignadas = new HashSet<Guid>(obrasAsignadas);
        }

        public Task<bool> EstaAsignadaACapatazAsync(
            Guid obraId,
            string? authorizationHeader,
            string? correlationId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_obrasAsignadas.Contains(obraId));

        public Task<IReadOnlyList<Guid>?> ObtenerObrasAsignadasAsync(
            string? authorizationHeader,
            string? correlationId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Guid>?>(_obrasAsignadas.ToList());
    }

    private sealed class ValidadorQueNoPuedeVerificar : IObraAsignacionValidator
    {
        public Task<bool> EstaAsignadaACapatazAsync(
            Guid obraId,
            string? authorizationHeader,
            string? correlationId,
            CancellationToken cancellationToken = default)
            => throw new ObraAsignacionNoVerificableException("Usuarios y Obras no respondió.");

        public Task<IReadOnlyList<Guid>?> ObtenerObrasAsignadasAsync(
            string? authorizationHeader,
            string? correlationId,
            CancellationToken cancellationToken = default)
            => throw new ObraAsignacionNoVerificableException("Usuarios y Obras no respondió.");
    }
}
