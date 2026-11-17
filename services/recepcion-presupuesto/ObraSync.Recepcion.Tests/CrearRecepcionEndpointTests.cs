
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ObraSync.Recepcion.Application.DTOs;
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
            new List<OrdenCompraItem> { articulo },
            fechaDespacho: DateTime.UtcNow);

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
        decimal cantidadRecibida = 100, DateTime? fechaRecepcion = null)
    {
        return new
        {
            obraId,
            ordenCompraId,
            numeroRemito = $"REM-{Guid.NewGuid():N}",
            fechaRecepcion = fechaRecepcion ?? DateTime.UtcNow,
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
    public async Task CrearRecepcion_EntregaParcial_Devuelve201YEstadoParcial()
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

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);

        var dto = await respuesta.Content.ReadFromJsonAsync<RecepcionDto>();
        Assert.NotNull(dto);
        Assert.Equal(EstadoRecepcion.Parcial, dto.Estado);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();

        var recepcion = await db.Recepciones
            .Include(r => r.Detalles)
            .SingleAsync(r => r.OrdenCompraId == ordenId);

        Assert.Equal(EstadoRecepcion.Parcial, recepcion.Estado);
        var detalle = Assert.Single(recepcion.Detalles);
        Assert.Equal(100m, detalle.CantidadEsperada);
        Assert.Equal(75m, detalle.CantidadRecibida);
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

    [Theory]
    [InlineData("Comprador")]
    [InlineData("Proveedor")]
    public async Task CrearRecepcion_RolNoPermitido_Devuelve403(string rol)
    {
        using var client = CrearClienteConToken(rol: rol);

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
            new List<OrdenCompraItem> { articulo1, articulo2 },
            fechaDespacho: DateTime.UtcNow);

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

    [Fact]
    public async Task CrearRecepcion_ConTokenInvalido_Devuelve401()
    {
        using var client = _factory.CreateClient();

        var token = RecepcionApiFactory.CreateToken(
            customSecret: "secreto-completamente-invalido-y-diferente!");

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var solicitud = CrearSolicitud(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid());

        var respuesta = await client.PostAsJsonAsync(
            "/api/recepciones", solicitud);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task CrearRecepcion_ConTokenExpirado_Devuelve401()
    {
        using var client = _factory.CreateClient();

        var token = RecepcionApiFactory.CreateToken(expired: true);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var solicitud = CrearSolicitud(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid());

        var respuesta = await client.PostAsJsonAsync(
            "/api/recepciones", solicitud);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task CrearRecepcion_FechaPermitida_Devuelve201(int dias)
    {
        var (ordenId, obraId, articuloId) = await CrearOrdenCompraDePrueba();

        using var client = CrearClienteConToken();

        DateTime fechaRecepcion = DateTime.UtcNow.Date.AddDays(dias);

        var solicitud = CrearSolicitud(
            ordenId,
            obraId,
            articuloId,
            fechaRecepcion: fechaRecepcion);

        var respuesta = await client.PostAsJsonAsync(
            "/api/recepciones", solicitud);

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
    }

    [Fact]
    public async Task CrearRecepcion_FechaFutura_Devuelve400()
    {
        var (ordenId, obraId, articuloId) = await CrearOrdenCompraDePrueba();

        using var client = CrearClienteConToken();

        DateTime fechaRecepcion = DateTime.UtcNow.Date.AddDays(2);

        var solicitud = CrearSolicitud(
            ordenId,
            obraId,
            articuloId,
            fechaRecepcion: fechaRecepcion);

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
    public async Task CrearRecepcion_OrdenNoDespachada_Devuelve409ConflictConProblemDetailsYCorrelationId()
    {
        var ordenCompraId = Guid.NewGuid();
        var obraId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        var correlationId = Guid.NewGuid().ToString();

        var articulo = OrdenCompraItem.Crear(articuloId, 100, "UNIDAD", 10, "Barras de hierro");
        var ordenSinDespachar = OrdenCompra.Crear(
            ordenCompraId,
            obraId,
            Guid.NewGuid(),
            1000,
            new List<OrdenCompraItem> { articulo }); // fechaDespacho = null

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();
            db.OrdenesCompra.Add(ordenSinDespachar);
            await db.SaveChangesAsync();
        }

        using var client = CrearClienteConToken();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);

        var solicitud = CrearSolicitud(ordenCompraId, obraId, articuloId);
        var respuesta = await client.PostAsJsonAsync("/api/recepciones", solicitud);

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);

        using var doc = System.Text.Json.JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        Assert.Equal(409, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("La orden de compra todavía no fue despachada", doc.RootElement.GetProperty("detail").GetString());
        Assert.Equal(correlationId, doc.RootElement.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task CrearRecepcion_ParcialLuegoSaldo_CompletaOrdenDesapareceDePendientesYRechazaTerceraCon409()
    {
        var (ordenId, obraId, articuloId) = await CrearOrdenCompraDePrueba();
        using var client = CrearClienteConToken();

        // 1. Primera entrega parcial: 75 de 100 -> 201 Created, Estado Parcial (2)
        var primeraSolicitud = CrearSolicitud(
            ordenId,
            obraId,
            articuloId,
            cantidadEsperada: 100,
            cantidadRecibida: 75);

        var primeraRespuesta = await client.PostAsJsonAsync("/api/recepciones", primeraSolicitud);
        Assert.Equal(HttpStatusCode.Created, primeraRespuesta.StatusCode);

        var primeraDto = await primeraRespuesta.Content.ReadFromJsonAsync<RecepcionDto>();
        Assert.NotNull(primeraDto);
        Assert.Equal(EstadoRecepcion.Parcial, primeraDto.Estado);

        // 2. La OC sigue en GET /api/recepciones/ordenes-pendientes con cantidadRecibida = 75
        var pendientesTrasPrimera = await (await client.GetAsync($"/api/recepciones/ordenes-pendientes?obraId={obraId}"))
            .Content.ReadFromJsonAsync<List<OrdenPendienteDto>>();
        Assert.NotNull(pendientesTrasPrimera);
        var pendiente = Assert.Single(pendientesTrasPrimera, o => o.OrdenCompraId == ordenId);
        var itemPendiente = Assert.Single(pendiente.Items);
        Assert.Equal(100m, itemPendiente.CantidadEsperada);
        Assert.Equal(75m, itemPendiente.CantidadRecibida);

        // 3. Segunda entrega por el saldo: 25 de 100 -> 201 Created, Estado Completa (3)
        var segundaSolicitud = CrearSolicitud(
            ordenId,
            obraId,
            articuloId,
            cantidadEsperada: 25,
            cantidadRecibida: 25);

        var segundaRespuesta = await client.PostAsJsonAsync("/api/recepciones", segundaSolicitud);
        Assert.Equal(HttpStatusCode.Created, segundaRespuesta.StatusCode);

        var segundaDto = await segundaRespuesta.Content.ReadFromJsonAsync<RecepcionDto>();
        Assert.NotNull(segundaDto);
        Assert.Equal(EstadoRecepcion.Completa, segundaDto.Estado);

        // 4. La OC desaparece de GET /api/recepciones/ordenes-pendientes
        var pendientesTrasSegunda = await (await client.GetAsync($"/api/recepciones/ordenes-pendientes?obraId={obraId}"))
            .Content.ReadFromJsonAsync<List<OrdenPendienteDto>>();
        Assert.NotNull(pendientesTrasSegunda);
        Assert.DoesNotContain(pendientesTrasSegunda, o => o.OrdenCompraId == ordenId);

        // 5. Intentar una tercera recepción devuelve 409 Conflict
        var terceraSolicitud = CrearSolicitud(
            ordenId,
            obraId,
            articuloId,
            cantidadEsperada: 100,
            cantidadRecibida: 10);

        var terceraRespuesta = await client.PostAsJsonAsync("/api/recepciones", terceraSolicitud);
        Assert.Equal(HttpStatusCode.Conflict, terceraRespuesta.StatusCode);
    }

    [Fact]
    public async Task CrearRecepcion_ParcialSeguidaDeExcesoAcumulado_Devuelve400YNoGuardaSegunda()
    {
        var (ordenId, obraId, articuloId) = await CrearOrdenCompraDePrueba();
        using var client = CrearClienteConToken();

        // Primera entrega: 75 de 100 -> 201 Created
        var primera = CrearSolicitud(ordenId, obraId, articuloId, cantidadEsperada: 100, cantidadRecibida: 75);
        var r1 = await client.PostAsJsonAsync("/api/recepciones", primera);
        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);

        // Segunda entrega: 30 de 100 (acumulado 105 > 100) -> 400 Bad Request
        var segunda = CrearSolicitud(ordenId, obraId, articuloId, cantidadEsperada: 100, cantidadRecibida: 30);
        var r2 = await client.PostAsJsonAsync("/api/recepciones", segunda);
        Assert.Equal(HttpStatusCode.BadRequest, r2.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();
        int totalRecepciones = await db.Recepciones.CountAsync(r => r.OrdenCompraId == ordenId);
        Assert.Equal(1, totalRecepciones);
    }

    [Fact]
    public async Task CrearRecepcion_DosArticulos_DosEntregasParciales_CompletaEnLaSegunda()
    {
        Guid ordenId = Guid.NewGuid();
        Guid obraId = Guid.NewGuid();
        Guid art1 = Guid.NewGuid();
        Guid art2 = Guid.NewGuid();

        var item1 = OrdenCompraItem.Crear(art1, 100m, "UNIDAD", 10m, "Barras de hierro");
        var item2 = OrdenCompraItem.Crear(art2, 50m, "BOLSA", 20m, "Cemento Portland");
        var oc = OrdenCompra.Crear(
            ordenId,
            obraId,
            Guid.NewGuid(),
            2000m,
            new List<OrdenCompraItem> { item1, item2 },
            fechaDespacho: DateTime.UtcNow);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();
            db.OrdenesCompra.Add(oc);
            await db.SaveChangesAsync();
        }

        using var client = CrearClienteConToken();

        // Entrega 1: completa art1 (100/100) y parcial art2 (20/50) -> Parcial
        var entrega1 = new
        {
            obraId,
            ordenCompraId = ordenId,
            numeroRemito = $"REM-E1-{Guid.NewGuid():N}",
            fechaRecepcion = DateTime.UtcNow,
            observaciones = "Primera entrega parcial multi-ítem",
            detalles = new[]
            {
                new { articuloId = art1, descripcionArticulo = "Barras de hierro", cantidadEsperada = 100m, cantidadRecibida = 100m, unidadMedida = "UNIDAD", conforme = true, observacion = (string?)null },
                new { articuloId = art2, descripcionArticulo = "Cemento Portland", cantidadEsperada = 50m, cantidadRecibida = 20m, unidadMedida = "BOLSA", conforme = true, observacion = (string?)null }
            }
        };

        var r1 = await client.PostAsJsonAsync("/api/recepciones", entrega1);
        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);
        var dto1 = await r1.Content.ReadFromJsonAsync<RecepcionDto>();
        Assert.NotNull(dto1);
        Assert.Equal(EstadoRecepcion.Parcial, dto1.Estado);

        // Entrega 2: 0 del art1 (ya completo) y 30 del art2 (cierra saldo de 50) -> Completa
        var entrega2 = new
        {
            obraId,
            ordenCompraId = ordenId,
            numeroRemito = $"REM-E2-{Guid.NewGuid():N}",
            fechaRecepcion = DateTime.UtcNow,
            observaciones = "Segunda entrega cierra saldo",
            detalles = new[]
            {
                new { articuloId = art1, descripcionArticulo = "Barras de hierro", cantidadEsperada = 0m, cantidadRecibida = 0m, unidadMedida = "UNIDAD", conforme = true, observacion = (string?)null },
                new { articuloId = art2, descripcionArticulo = "Cemento Portland", cantidadEsperada = 30m, cantidadRecibida = 30m, unidadMedida = "BOLSA", conforme = true, observacion = (string?)null }
            }
        };

        var r2 = await client.PostAsJsonAsync("/api/recepciones", entrega2);
        Assert.Equal(HttpStatusCode.Created, r2.StatusCode);
        var dto2 = await r2.Content.ReadFromJsonAsync<RecepcionDto>();
        Assert.NotNull(dto2);
        Assert.Equal(EstadoRecepcion.Completa, dto2.Estado);
    }

    [Fact]
    public async Task CrearRecepcion_TodosLosItemsConCantidadRecibidaCero_Devuelve400()
    {
        var (ordenId, obraId, articuloId) = await CrearOrdenCompraDePrueba();
        using var client = CrearClienteConToken();

        var solicitud = CrearSolicitud(
            ordenId,
            obraId,
            articuloId,
            cantidadEsperada: 100,
            cantidadRecibida: 0);

        var respuesta = await client.PostAsJsonAsync("/api/recepciones", solicitud);
        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }
}
