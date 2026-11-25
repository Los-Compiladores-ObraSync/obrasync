using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using MassTransit;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using ObraSync.Contracts.Events;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.UseCases.Chat.Commands;
using ObraSync.Licitaciones.Application.UseCases.Chat.Handlers;
using ObraSync.Licitaciones.Domain.Entities;
using ObraSync.Licitaciones.Infrastructure;
using ObraSync.Licitaciones.Infrastructure.Messaging;
using Xunit;

namespace ObraSync.Licitaciones.Tests;

/// <summary>
/// Tarjeta #232 + Decisión #233:
/// - Calificar proveedores (por orden #222 y por proveedor #88): escritura SOLO Comprador; lectura Comprador/Admin.
/// - Cotizar (POST /api/cotizaciones y PUT /api/cotizaciones/{id}): escritura SOLO Proveedor.
/// - Chat de órdenes de compra: GET permitido para Comprador, Proveedor dueño y Admin (solo lectura / vigía);
///   POST escritura SOLO Comprador y Proveedor dueño (Admin recibe 403 Forbidden con ProblemDetails y correlationId).
/// </summary>
public class RestriccionAdminCalificarCotizarYChatTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestJwtSecret = "clave-de-pruebas-de-al-menos-32-caracteres";
    private readonly WebApplicationFactory<Program> _baseFactory;

    public RestriccionAdminCalificarCotizarYChatTests(WebApplicationFactory<Program> factory)
    {
        Environment.SetEnvironmentVariable("Jwt__Secret", TestJwtSecret);
        _baseFactory = factory;
    }

    private WebApplicationFactory<Program> CreateIsolatedFactory(
        out IPublishEndpoint publishSpy,
        Action<LicitacionesDbContext>? seedAction = null)
    {
        var dbName = $"DB_Licit_Rbac232_{Guid.NewGuid()}";
        var spy = Substitute.For<IPublishEndpoint>();
        publishSpy = spy;

        var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:Secret", TestJwtSecret);
            builder.UseSetting("RabbitMQ:UseInMemory", "true");

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();

                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<LicitacionesDbContext>));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                services.RemoveAll<IDbContextOptionsConfiguration<LicitacionesDbContext>>();

                services.AddDbContext<LicitacionesDbContext>(options =>
                {
                    options.UseInMemoryDatabase(dbName);
                });

                services.RemoveAll<IPublishEndpoint>();
                services.AddSingleton(spy);

                if (seedAction != null)
                {
                    using var sp = services.BuildServiceProvider();
                    using var scope = sp.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
                    seedAction(db);
                    db.SaveChanges();
                }
            });
        });

        _ = factory.Server;
        return factory;
    }

    private static string GenerarTokenJwt(string role, Guid? userId = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim("sub", (userId ?? Guid.NewGuid()).ToString()),
            new Claim("email", $"{role.ToLowerInvariant()}@obrasync.com"),
            new Claim("name", $"Usuario {role}"),
            new Claim("role", role)
        };

        var token = new JwtSecurityToken(
            issuer: "obrasync-usuarios",
            audience: "obrasync-services",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static HttpClient CrearCliente(
        WebApplicationFactory<Program> factory,
        string? rol,
        Guid? userId = null,
        string? correlationId = null)
    {
        var client = factory.CreateClient();
        if (correlationId is not null)
        {
            client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
        }

        if (rol is not null)
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerarTokenJwt(rol, userId));
        }

        return client;
    }

    private static async Task AssertProblemDetailsWithCorrelationIdAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCorrelationId)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal((int)expectedStatus, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(expectedCorrelationId, doc.RootElement.GetProperty("correlationId").GetString());
    }

    // ---- 1. Calificar por orden (#222): SOLO Comprador ----

    [Fact]
    public async Task CalificarOrden_SoloCompradorTienePermiso_AdminProveedorCapatazReciben403YSinToken401()
    {
        var ordenId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();
        var licId = Guid.NewGuid();

        using var factory = CreateIsolatedFactory(out _, db =>
        {
            db.Licitaciones.Add(new Licitacion
            {
                LicitacionId = licId,
                RequisicionId = Guid.NewGuid(),
                NumeroRequisicion = "REQ-2026-2321",
                ObraId = Guid.NewGuid(),
                Estado = EstadoLicitacion.Adjudicada,
                FechaCreacion = DateTime.UtcNow.AddDays(-3),
                FechaLimiteCotizacion = DateTime.UtcNow.AddDays(-1)
            });
            db.OrdenesCompra.Add(new OrdenCompra
            {
                OrdenCompraId = ordenId,
                LicitacionId = licId,
                CotizacionId = Guid.NewGuid(),
                ProveedorId = proveedorId,
                Estado = "Despachada",
                FechaEmision = DateTime.UtcNow.AddDays(-2),
                FechaDespacho = DateTime.UtcNow.AddDays(-1),
                RecibidaCompleta = true,
                FechaRecepcionCompleta = DateTime.UtcNow.AddHours(-2)
            });
        });

        var payload = new UpsertCalificacionOrdenCompraRequestDto(5, "Excelente entrega");

        // Sin token -> 401
        var cid401 = Guid.NewGuid().ToString();
        var respSinToken = await CrearCliente(factory, null, correlationId: cid401)
            .PutAsJsonAsync($"/api/ordenes-compra/{ordenId}/calificacion", payload);
        await AssertProblemDetailsWithCorrelationIdAsync(respSinToken, HttpStatusCode.Unauthorized, cid401);

        // Admin, Proveedor y Capataz -> 403 con ProblemDetails y correlationId
        foreach (var rol in new[] { "Admin", "Proveedor", "Capataz" })
        {
            var cid = Guid.NewGuid().ToString();
            var uid = rol == "Proveedor" ? proveedorId : (Guid?)null;
            var resp = await CrearCliente(factory, rol, uid, cid)
                .PutAsJsonAsync($"/api/ordenes-compra/{ordenId}/calificacion", payload);
            await AssertProblemDetailsWithCorrelationIdAsync(resp, HttpStatusCode.Forbidden, cid);
        }

        // Comprador -> 200 OK
        var respComprador = await CrearCliente(factory, "Comprador")
            .PutAsJsonAsync($"/api/ordenes-compra/{ordenId}/calificacion", payload);
        Assert.Equal(HttpStatusCode.OK, respComprador.StatusCode);

        // Admin sigue pudiendo consultar calificaciones (lectura/auditoría -> no 403)
        var respGetAdmin = await CrearCliente(factory, "Admin")
            .GetAsync($"/api/ordenes-compra/{ordenId}/calificacion");
        Assert.NotEqual(HttpStatusCode.Forbidden, respGetAdmin.StatusCode);
    }

    // ---- 2. Calificar por proveedor (#88): SOLO Comprador ----

    [Fact]
    public async Task CalificarProveedor_SoloCompradorTienePermiso_AdminProveedorCapatazReciben403YSinToken401()
    {
        var proveedorId = Guid.NewGuid();
        using var factory = CreateIsolatedFactory(out _);

        var payload = new UpsertCalificacionProveedorRequestDto(4);

        // Sin token -> 401
        var cid401 = Guid.NewGuid().ToString();
        var respSinToken = await CrearCliente(factory, null, correlationId: cid401)
            .PutAsJsonAsync($"/api/proveedores/{proveedorId}/calificacion", payload);
        await AssertProblemDetailsWithCorrelationIdAsync(respSinToken, HttpStatusCode.Unauthorized, cid401);

        // Admin, Proveedor y Capataz -> 403 en ambas rutas (/api/proveedores y /api/cotizaciones/proveedores)
        foreach (var rol in new[] { "Admin", "Proveedor", "Capataz" })
        {
            var cid1 = Guid.NewGuid().ToString();
            var r1 = await CrearCliente(factory, rol, correlationId: cid1)
                .PutAsJsonAsync($"/api/proveedores/{proveedorId}/calificacion", payload);
            await AssertProblemDetailsWithCorrelationIdAsync(r1, HttpStatusCode.Forbidden, cid1);

            var cid2 = Guid.NewGuid().ToString();
            var r2 = await CrearCliente(factory, rol, correlationId: cid2)
                .PutAsJsonAsync($"/api/cotizaciones/proveedores/{proveedorId}/calificacion", payload);
            await AssertProblemDetailsWithCorrelationIdAsync(r2, HttpStatusCode.Forbidden, cid2);
        }

        // Comprador -> 200 OK
        var respComprador = await CrearCliente(factory, "Comprador")
            .PutAsJsonAsync($"/api/cotizaciones/proveedores/{proveedorId}/calificacion", payload);
        Assert.Equal(HttpStatusCode.OK, respComprador.StatusCode);

        // Admin sigue pudiendo leer la calificación (GET -> 200 OK)
        var respGetAdmin = await CrearCliente(factory, "Admin")
            .GetAsync($"/api/cotizaciones/proveedores/{proveedorId}/calificacion");
        Assert.Equal(HttpStatusCode.OK, respGetAdmin.StatusCode);
    }

    // ---- 3. Cotizar (POST /api/cotizaciones y PUT /api/cotizaciones/{id}): SOLO Proveedor ----

    [Fact]
    public async Task Cotizar_SoloProveedorTienePermiso_AdminCompradorCapatazReciben403YSinToken401()
    {
        var licitacionId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();

        using var factory = CreateIsolatedFactory(out _, db =>
        {
            db.Licitaciones.Add(new Licitacion
            {
                LicitacionId = licitacionId,
                RequisicionId = Guid.NewGuid(),
                NumeroRequisicion = "REQ-2026-2322",
                ObraId = Guid.NewGuid(),
                Estado = EstadoLicitacion.Abierta,
                FechaCreacion = DateTime.UtcNow.AddHours(-2),
                FechaLimiteCotizacion = DateTime.UtcNow.AddDays(3),
                Items = new List<LicitacionItem>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        LicitacionId = licitacionId,
                        ArticuloId = articuloId,
                        Descripcion = "Cemento Portland",
                        Cantidad = 25m,
                        UnidadMedida = "BOLSA"
                    }
                }
            });
        });

        var request = new CrearCotizacionRequestDto
        {
            LicitacionId = licitacionId,
            ProveedorId = proveedorId,
            Flete = 500m,
            PlazoEntregaDias = 2,
            Items = new List<CrearCotizacionItemDto>
            {
                new() { ArticuloId = articuloId, PrecioUnitario = 1200m }
            }
        };

        // Sin token -> 401
        var cid401 = Guid.NewGuid().ToString();
        var respSinToken = await CrearCliente(factory, null, correlationId: cid401)
            .PostAsJsonAsync("/api/cotizaciones", request);
        await AssertProblemDetailsWithCorrelationIdAsync(respSinToken, HttpStatusCode.Unauthorized, cid401);

        // Admin, Comprador y Capataz -> 403 con ProblemDetails y correlationId
        foreach (var rol in new[] { "Admin", "Comprador", "Capataz" })
        {
            var cid = Guid.NewGuid().ToString();
            var resp = await CrearCliente(factory, rol, correlationId: cid)
                .PostAsJsonAsync("/api/cotizaciones", request);
            await AssertProblemDetailsWithCorrelationIdAsync(resp, HttpStatusCode.Forbidden, cid);
        }

        // Proveedor -> 201 Created
        var respProveedor = await CrearCliente(factory, "Proveedor", proveedorId)
            .PostAsJsonAsync("/api/cotizaciones", request);
        Assert.Equal(HttpStatusCode.Created, respProveedor.StatusCode);

        var creada = await respProveedor.Content.ReadFromJsonAsync<CotizacionResponseDto>();
        Assert.NotNull(creada);

        // PUT /api/cotizaciones/{id} (corregir cotización): Admin también recibe 403
        var cidPutAdmin = Guid.NewGuid().ToString();
        var respPutAdmin = await CrearCliente(factory, "Admin", correlationId: cidPutAdmin)
            .PutAsJsonAsync($"/api/cotizaciones/{creada.CotizacionId}", request);
        await AssertProblemDetailsWithCorrelationIdAsync(respPutAdmin, HttpStatusCode.Forbidden, cidPutAdmin);
    }

    // ---- 4. Chat de órdenes (Decisión #233): Solo lectura para Admin ----

    [Fact]
    public async Task ChatOrden_AdminSoloLectura_CompradorYProveedorEscribenYLeen_Capataz403_SinToken401()
    {
        var ordenId = Guid.NewGuid();
        var licId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();
        var compradorId = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        using var factory = CreateIsolatedFactory(out var publishSpy, db =>
        {
            db.Licitaciones.Add(new Licitacion
            {
                LicitacionId = licId,
                RequisicionId = Guid.NewGuid(),
                NumeroRequisicion = "REQ-2026-2323",
                ObraId = Guid.NewGuid(),
                Estado = EstadoLicitacion.Adjudicada,
                FechaCreacion = DateTime.UtcNow.AddDays(-1),
                FechaLimiteCotizacion = DateTime.UtcNow.AddDays(2)
            });
            db.OrdenesCompra.Add(new OrdenCompra
            {
                OrdenCompraId = ordenId,
                LicitacionId = licId,
                CotizacionId = Guid.NewGuid(),
                ProveedorId = proveedorId,
                Estado = "Emitida",
                FechaEmision = DateTime.UtcNow
            });
        });

        // Sin token: GET y POST -> 401
        var cidGet401 = Guid.NewGuid().ToString();
        var respGet401 = await CrearCliente(factory, null, correlationId: cidGet401)
            .GetAsync($"/api/ordenes-compra/{ordenId}/chat/mensajes");
        await AssertProblemDetailsWithCorrelationIdAsync(respGet401, HttpStatusCode.Unauthorized, cidGet401);

        var cidPost401 = Guid.NewGuid().ToString();
        var respPost401 = await CrearCliente(factory, null, correlationId: cidPost401)
            .PostAsJsonAsync($"/api/ordenes-compra/{ordenId}/chat/mensajes", new { texto = "Sin auth" });
        await AssertProblemDetailsWithCorrelationIdAsync(respPost401, HttpStatusCode.Unauthorized, cidPost401);

        // Capataz: GET y POST -> 403
        var cidCapGet = Guid.NewGuid().ToString();
        var respCapGet = await CrearCliente(factory, "Capataz", correlationId: cidCapGet)
            .GetAsync($"/api/ordenes-compra/{ordenId}/chat/mensajes");
        await AssertProblemDetailsWithCorrelationIdAsync(respCapGet, HttpStatusCode.Forbidden, cidCapGet);

        var cidCapPost = Guid.NewGuid().ToString();
        var respCapPost = await CrearCliente(factory, "Capataz", correlationId: cidCapPost)
            .PostAsJsonAsync($"/api/ordenes-compra/{ordenId}/chat/mensajes", new { texto = "Hola" });
        await AssertProblemDetailsWithCorrelationIdAsync(respCapPost, HttpStatusCode.Forbidden, cidCapPost);

        // Comprador y Proveedor dueño escriben -> 201 Created
        var respComp = await CrearCliente(factory, "Comprador", compradorId)
            .PostAsJsonAsync($"/api/ordenes-compra/{ordenId}/chat/mensajes", new { texto = "Confirmen horario de descarga" });
        Assert.Equal(HttpStatusCode.Created, respComp.StatusCode);

        var respProv = await CrearCliente(factory, "Proveedor", proveedorId)
            .PostAsJsonAsync($"/api/ordenes-compra/{ordenId}/chat/mensajes", new { texto = "Llegamos mañana a las 9" });
        Assert.Equal(HttpStatusCode.Created, respProv.StatusCode);

        // Comprador y Proveedor dueño leen -> 200 OK (2 mensajes)
        var mensajesComp = await CrearCliente(factory, "Comprador", compradorId)
            .GetFromJsonAsync<List<MensajeChatOrdenDto>>($"/api/ordenes-compra/{ordenId}/chat/mensajes");
        Assert.NotNull(mensajesComp);
        Assert.Equal(2, mensajesComp.Count);

        var mensajesProv = await CrearCliente(factory, "Proveedor", proveedorId)
            .GetFromJsonAsync<List<MensajeChatOrdenDto>>($"/api/ordenes-compra/{ordenId}/chat/mensajes");
        Assert.NotNull(mensajesProv);
        Assert.Equal(2, mensajesProv.Count);

        // Admin leyendo el chat (GET) -> 200 OK (modo solo lectura / vigía)
        var respAdminGet = await CrearCliente(factory, "Admin", adminId)
            .GetAsync($"/api/ordenes-compra/{ordenId}/chat/mensajes");
        Assert.Equal(HttpStatusCode.OK, respAdminGet.StatusCode);
        var mensajesAdmin = await respAdminGet.Content.ReadFromJsonAsync<List<MensajeChatOrdenDto>>();
        Assert.NotNull(mensajesAdmin);
        Assert.Equal(2, mensajesAdmin.Count);

        // Admin intentando escribir en el chat (POST) -> 403 Forbidden con ProblemDetails y correlationId
        var cidAdminPost = Guid.NewGuid().ToString();
        var respAdminPost = await CrearCliente(factory, "Admin", adminId, cidAdminPost)
            .PostAsJsonAsync($"/api/ordenes-compra/{ordenId}/chat/mensajes", new { texto = "Mensaje no permitido" });
        await AssertProblemDetailsWithCorrelationIdAsync(respAdminPost, HttpStatusCode.Forbidden, cidAdminPost);

        // Verificar que Admin no persistió mensaje ni publicó evento adicional
        await publishSpy.DidNotReceive().Publish<MensajeChatEnviadoEvent>(
            Arg.Is<MensajeChatEnviadoEvent>(e => e.Remitente.Rol == "Admin"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnviarMensajeChatHandler_RolAdmin_LanzaUnauthorizedAccessExceptionYNoGuardaNiPublica()
    {
        var ordenId = Guid.NewGuid();
        var ordenRepo = Substitute.For<IOrdenCompraRepository>();
        var chatRepo = Substitute.For<IChatOrdenRepository>();
        var publish = Substitute.For<IPublishEndpoint>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        ordenRepo.GetByIdAsync(ordenId, Arg.Any<CancellationToken>())
            .Returns(new OrdenCompra
            {
                OrdenCompraId = ordenId,
                LicitacionId = Guid.NewGuid(),
                CotizacionId = Guid.NewGuid(),
                ProveedorId = Guid.NewGuid(),
                Estado = "Emitida"
            });

        var handler = new EnviarMensajeChatOrdenCommandHandler(ordenRepo, chatRepo, new MassTransitLicitacionEventPublisher(publish), unitOfWork);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(
                new EnviarMensajeChatOrdenCommand(ordenId, Guid.NewGuid(), "Admin", "Admin", "No debería poder escribir"),
                CancellationToken.None));

        await chatRepo.DidNotReceive().AddMensajeAsync(Arg.Any<MensajeChatOrden>(), Arg.Any<CancellationToken>());
        await publish.DidNotReceive().Publish<MensajeChatEnviadoEvent>(Arg.Any<MensajeChatEnviadoEvent>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
