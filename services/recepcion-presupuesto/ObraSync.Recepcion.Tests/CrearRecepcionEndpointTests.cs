
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Domain.Enums;
using ObraSync.Recepcion.Infrastructure.Persistence;

namespace ObraSync.Recepcion.Tests;

public class CrearRecepcionEndpointTests : IClassFixture<RecepcionApiFactory>
{
    private readonly RecepcionApiFactory _factory;

    public CrearRecepcionEndpointTests(RecepcionApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<(Guid ordenCompraId, Guid obraId, Guid articuloId)>
        CrearOrdenCompraDePrueba()
    {
        Guid ordenCompraId = Guid.NewGuid();
        Guid obraId = Guid.NewGuid();
        Guid articuloId = Guid.NewGuid();

        var articulo = OrdenCompraItem.Crear(
            articuloId,
            100,
            "UNIDAD",
            10,
            "Barras de hierro");

        var ordenCompra = OrdenCompra.Crear(
            ordenCompraId,
            obraId,
            Guid.NewGuid(),
            1000,
            new List<OrdenCompraItem> { articulo });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();

        db.OrdenesCompra.Add(ordenCompra);
        await db.SaveChangesAsync();

        return (ordenCompraId, obraId, articuloId);
    }

    private HttpClient CrearClienteConToken(
        string rol = "Capataz",
        Guid? usuarioId = null)
    {
        var client = _factory.CreateClient();

        string token = RecepcionApiFactory.CreateToken(
            role: rol,
            userId: usuarioId);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    private static object CrearSolicitud(
        Guid ordenCompraId,
        Guid obraId,
        Guid articuloId,
        decimal cantidadEsperada = 100,
        decimal cantidadRecibida = 100)
    {
        return new
        {
            obraId,
            ordenCompraId,
            numeroRemito = $"REM-{Guid.NewGuid():N}",
            fechaRecepcion = DateTime.UtcNow,
            observaciones = "Entrega de materiales",
            detalles = new[]
            {
                new
                {
                    articuloId,
                    descripcionArticulo = "Barras de hierro",
                    cantidadEsperada,
                    cantidadRecibida,
                    unidadMedida = "UNIDAD",
                    conforme = true,
                    observacion = (string?)null
                }
            }
        };
    }

    [Fact]
    public async Task CrearRecepcion_Total_GuardaRecepcionYDevuelve201()
    {
        var (ordenId, obraId, articuloId) = await CrearOrdenCompraDePrueba();
        Guid usuarioId = Guid.NewGuid();

        using var client = CrearClienteConToken(usuarioId: usuarioId);

        var solicitud = CrearSolicitud(ordenId, obraId, articuloId);

        var respuesta = await client.PostAsJsonAsync(
            "/api/recepciones", solicitud);

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();

        var recepcion = await db.Recepciones
            .Include(r => r.Detalles)
            .SingleAsync(r => r.OrdenCompraId == ordenId);

        Assert.Equal(EstadoRecepcion.Completa, recepcion.Estado);
        Assert.Equal(usuarioId, recepcion.RecepcionadoPorUsuarioId);

        var detalle = Assert.Single(recepcion.Detalles);
        Assert.Equal(articuloId, detalle.ArticuloId);
        Assert.Equal(100m, detalle.CantidadEsperada);
        Assert.Equal(100m, detalle.CantidadRecibida);
    }

    [Fact]
    public async Task CrearRecepcion_OrdenInexistente_Devuelve400()
    {
        using var client = CrearClienteConToken();

        var solicitud = CrearSolicitud(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid());

        var respuesta = await client.PostAsJsonAsync(
            "/api/recepciones", solicitud);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    [Fact]
    public async Task CrearRecepcion_ArticuloNoPerteneceAOC_Devuelve400()
    {
        var (ordenId, obraId, _) = await CrearOrdenCompraDePrueba();
        using var client = CrearClienteConToken();

        var solicitud = CrearSolicitud(
            ordenId,
            obraId,
            Guid.NewGuid());

        var respuesta = await client.PostAsJsonAsync(
            "/api/recepciones", solicitud);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    [Fact]
    public async Task CrearRecepcion_CantidadEsperadaIncorrecta_Devuelve400()
    {
        var (ordenId, obraId, articuloId) = await CrearOrdenCompraDePrueba();
        using var client = CrearClienteConToken();

        var solicitud = CrearSolicitud(
            ordenId,
            obraId,
            articuloId,
            cantidadEsperada: 80,
            cantidadRecibida: 100);

        var respuesta = await client.PostAsJsonAsync(
            "/api/recepciones", solicitud);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    [Fact]
    public async Task CrearRecepcion_EntregaParcial_Devuelve400()
    {
        var (ordenId, obraId, articuloId) = await CrearOrdenCompraDePrueba();
        using var client = CrearClienteConToken();

        var solicitud = CrearSolicitud(
            ordenId,
            obraId,
            articuloId,
            cantidadEsperada: 100,
            cantidadRecibida: 75);

        var respuesta = await client.PostAsJsonAsync(
            "/api/recepciones", solicitud);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();

        bool seGuardo = await db.Recepciones
            .AnyAsync(r => r.OrdenCompraId == ordenId);

        Assert.False(seGuardo);
    }

    [Fact]
    public async Task CrearRecepcion_OCYaRecibida_Devuelve409()
    {
        var (ordenId, obraId, articuloId) = await CrearOrdenCompraDePrueba();
        using var client = CrearClienteConToken();

        var primeraSolicitud = CrearSolicitud(ordenId, obraId, articuloId);

        var primeraRespuesta = await client.PostAsJsonAsync(
            "/api/recepciones", primeraSolicitud);

        Assert.Equal(HttpStatusCode.Created, primeraRespuesta.StatusCode);

        var segundaSolicitud = CrearSolicitud(ordenId, obraId, articuloId);

        var segundaRespuesta = await client.PostAsJsonAsync(
            "/api/recepciones", segundaSolicitud);

        Assert.Equal(HttpStatusCode.Conflict, segundaRespuesta.StatusCode);
    }

    [Fact]
    public async Task CrearRecepcion_SinToken_Devuelve401()
    {
        using var client = _factory.CreateClient();

        var solicitud = CrearSolicitud(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid());

        var respuesta = await client.PostAsJsonAsync(
            "/api/recepciones", solicitud);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task CrearRecepcion_RolNoPermitido_Devuelve403()
    {
        using var client = CrearClienteConToken(rol: "Comprador");

        var solicitud = CrearSolicitud(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid());

        var respuesta = await client.PostAsJsonAsync(
            "/api/recepciones", solicitud);

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task CrearRecepcion_AdminPuedeRegistrar_Devuelve201()
    {
        var (ordenId, obraId, articuloId) = await CrearOrdenCompraDePrueba();
        using var client = CrearClienteConToken(rol: "Admin");

        var solicitud = CrearSolicitud(ordenId, obraId, articuloId);

        var respuesta = await client.PostAsJsonAsync(
            "/api/recepciones", solicitud);

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
    }

    // PRUEBA NUEVA 1: No recibir más materiales de los comprados.
    [Fact]
    public async Task CrearRecepcion_CantidadRecibidaMayor_Devuelve400()
    {
        var (ordenId, obraId, articuloId) = await CrearOrdenCompraDePrueba();

        using var client = CrearClienteConToken();

        var solicitud = CrearSolicitud(
            ordenId,
            obraId,
            articuloId,
            cantidadEsperada: 100,
            cantidadRecibida: 120);

        var respuesta = await client.PostAsJsonAsync(
            "/api/recepciones", solicitud);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();

        bool seGuardo = await db.Recepciones
            .AnyAsync(r => r.OrdenCompraId == ordenId);

        Assert.False(seGuardo);
    }

    // PRUEBA NUEVA 2: Los errores deben devolver ProblemDetails.
    [Fact]
    public async Task CrearRecepcion_OrdenInexistente_DevuelveProblemDetails()
    {
        using var client = CrearClienteConToken();

        var solicitud = CrearSolicitud(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid());

        var respuesta = await client.PostAsJsonAsync(
            "/api/recepciones", solicitud);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);

        Assert.Equal(
            "application/problem+json",
            respuesta.Content.Headers.ContentType?.MediaType);

        var problema = await respuesta.Content.ReadFromJsonAsync<
            Microsoft.AspNetCore.Mvc.ProblemDetails>();

        Assert.NotNull(problema);
        Assert.Equal(400, problema.Status);
        Assert.Equal("La orden de compra no existe.", problema.Detail);
    }

    // PRUEBA NUEVA 3: Una recepción total debe incluir todos los artículos.
    [Fact]
    public async Task CrearRecepcion_FaltaUnMaterial_Devuelve400()
    {
        Guid ordenId = Guid.NewGuid();
        Guid obraId = Guid.NewGuid();
        Guid articuloId1 = Guid.NewGuid();
        Guid articuloId2 = Guid.NewGuid();

        var articulo1 = OrdenCompraItem.Crear(
            articuloId1,
            100,
            "UNIDAD",
            10,
            "Barras de hierro");

        var articulo2 = OrdenCompraItem.Crear(
            articuloId2,
            50,
            "UNIDAD",
            20,
            "Bolsas de cemento");

        var ordenCompra = OrdenCompra.Crear(
            ordenId,
            obraId,
            Guid.NewGuid(),
            2000,
            new List<OrdenCompraItem> { articulo1, articulo2 });

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();

            db.OrdenesCompra.Add(ordenCompra);
            await db.SaveChangesAsync();
        }

        using var client = CrearClienteConToken();

        // Enviamos solamente el primer artículo, aunque la OC tiene dos.
        var solicitud = CrearSolicitud(ordenId, obraId, articuloId1);

        var respuesta = await client.PostAsJsonAsync(
            "/api/recepciones", solicitud);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);

        using var verificarScope = _factory.Services.CreateScope();
        var verificarDb = verificarScope.ServiceProvider
            .GetRequiredService<RecepcionDbContext>();

        bool seGuardo = await verificarDb.Recepciones
            .AnyAsync(r => r.OrdenCompraId == ordenId);

        Assert.False(seGuardo);
    }
}
