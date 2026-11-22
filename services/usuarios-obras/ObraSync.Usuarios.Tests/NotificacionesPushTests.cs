using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using ObraSync.Contracts.Events;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Application.Interfaces;
using ObraSync.Usuarios.Application.Services;
using ObraSync.Usuarios.Application.Settings;
using ObraSync.Usuarios.Domain.Entities;
using ObraSync.Usuarios.Infrastructure.Consumers;
using ObraSync.Usuarios.Infrastructure.Data;
using ObraSync.Usuarios.Infrastructure.Push;
using ObraSync.Usuarios.Infrastructure.Repositories;
using ObraSync.Usuarios.Infrastructure.Services;
using Xunit;

namespace ObraSync.Usuarios.Tests;

public class NotificacionesPushTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestJwtSecret = "ClaveSecretaDePruebaParaUsuariosYObrasMinimo32Bytes!";
    private readonly WebApplicationFactory<Program> _baseFactory;

    public NotificacionesPushTests(WebApplicationFactory<Program> factory)
    {
        Environment.SetEnvironmentVariable("Jwt__Secret", TestJwtSecret);
        _baseFactory = factory;
    }

    private WebApplicationFactory<Program> CreateIsolatedFactory(
        out string dbName,
        IWebPushSender? fakePushSender = null,
        string? vapidPublicKey = "BPu-test-public-key-vapid-227",
        string? vapidPrivateKey = "test-private-key-vapid-secret-227",
        string environmentName = "Development",
        ILoggerProvider? loggerProvider = null,
        Action<AuthDbContext>? seedAction = null)
    {
        dbName = $"DB_Auth_Push_{Guid.NewGuid()}";
        var nombreDb = dbName;
        var sender = fakePushSender ?? Substitute.For<IWebPushSender>();

        return _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environmentName);
            builder.UseSetting("Jwt:Secret", TestJwtSecret);
            builder.UseSetting("ConnectionStrings:DefaultConnection", "InMemoryTestDb");
            builder.UseSetting("RabbitMQ:UseInMemory", "true");
            builder.UseSetting("Vapid:PublicKey", vapidPublicKey ?? string.Empty);
            builder.UseSetting("Vapid:PrivateKey", vapidPrivateKey ?? string.Empty);
            builder.UseSetting("Vapid:Subject", "mailto:test@obrasync.local");

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();

                if (loggerProvider is not null)
                {
                    services.AddSingleton<ILoggerProvider>(loggerProvider);
                }

                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<AuthDbContext>));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<AuthDbContext>(options =>
                {
                    options.UseInMemoryDatabase(nombreDb);
                });

                services.RemoveAll<IWebPushSender>();
                services.AddSingleton(sender);

                if (seedAction != null)
                {
                    using var sp = services.BuildServiceProvider();
                    using var scope = sp.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
                    db.Database.EnsureCreated();
                    seedAction(db);
                    db.SaveChanges();
                }
            });
        });
    }

    private static string GenerarTokenJwt(string role, Guid? userId = null, string? name = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim("sub", (userId ?? Guid.NewGuid()).ToString()),
            new Claim("email", $"{role.ToLowerInvariant()}@obrasync.com"),
            new Claim("name", name ?? $"Usuario {role}"),
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

    private static async Task SembrarUsuarioConRolAsync(
        AuthDbContext db,
        Guid userId,
        string email,
        string nombre,
        string roleName,
        bool activo = true,
        bool conSuscripcion = true)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == roleName);
        if (role is null)
        {
            role = new AppRole
            {
                Id = Guid.NewGuid(),
                Name = roleName,
                NormalizedName = roleName.ToUpperInvariant()
            };
            db.Roles.Add(role);
        }

        db.Users.Add(new AppUser
        {
            Id = userId,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NombreCompleto = nombre,
            Activo = activo,
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N")
        });

        db.UserRoles.Add(new IdentityUserRole<Guid>
        {
            UserId = userId,
            RoleId = role.Id
        });

        if (conSuscripcion)
        {
            db.SuscripcionesPush.Add(SuscripcionPush.Crear(
                usuarioId: userId,
                endpoint: $"https://push.obrasync.test/sub/{userId:N}",
                p256dh: "p256-test-key",
                auth: "auth-test-key",
                navegador: "Chrome"));
        }
    }

    private static (string PublicKey, string PrivateKey) GenerarParClavesEcBase64Url()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = ecdsa.ExportParameters(includePrivateParameters: true);
        var uncompressedPublic = new byte[1 + p.Q.X!.Length + p.Q.Y!.Length];
        uncompressedPublic[0] = 0x04;
        Buffer.BlockCopy(p.Q.X, 0, uncompressedPublic, 1, p.Q.X.Length);
        Buffer.BlockCopy(p.Q.Y, 0, uncompressedPublic, 1 + p.Q.X.Length, p.Q.Y.Length);

        return (Base64UrlEncode(uncompressedPublic), Base64UrlEncode(p.D!));
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    public async Task EndpointsVapidYSuscripciones_ValidanAuthValidacionesPrivacidadYHealthSwagger()
    {
        var userId = Guid.NewGuid();
        var privateSecret = "super-secreto-vapid-privado-227";

        // 1. Sin VAPID configurado -> /vapid-public-key responde 503 ProblemDetails, Swagger y Health siguen 200 OK
        using (var factorySinVapid = CreateIsolatedFactory(out _, vapidPublicKey: "", vapidPrivateKey: ""))
        {
            var client = factorySinVapid.CreateClient();
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/swagger/v1/swagger.json")).StatusCode);

            var cid503 = Guid.NewGuid().ToString();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador", userId));
            client.DefaultRequestHeaders.Add("X-Correlation-ID", cid503);

            var respVapid503 = await client.GetAsync("/api/usuarios/notificaciones/vapid-public-key");
            await AssertProblemDetailsWithCorrelationIdAsync(respVapid503, HttpStatusCode.ServiceUnavailable, cid503);
        }

        // 2. Con VAPID configurado -> 401 sin token, 200 con token (jamás expone PrivateKey), y validaciones 400
        var loggerProvider = new CapturaLogsPushProvider();
        using var factory = CreateIsolatedFactory(
            out _,
            vapidPublicKey: "BPu-public-key-visible-227",
            vapidPrivateKey: privateSecret,
            loggerProvider: loggerProvider,
            seedAction: db =>
            {
                db.Users.Add(new AppUser
                {
                    Id = userId,
                    UserName = "comp@obrasync.test",
                    NormalizedUserName = "COMP@OBRASYNC.TEST",
                    Email = "comp@obrasync.test",
                    NormalizedEmail = "COMP@OBRASYNC.TEST",
                    NombreCompleto = "Comprador Test",
                    Activo = true
                });
            });

        var clientAnon = factory.CreateClient();
        var cidAnon = Guid.NewGuid().ToString();
        clientAnon.DefaultRequestHeaders.Add("X-Correlation-ID", cidAnon);

        await AssertProblemDetailsWithCorrelationIdAsync(
            await clientAnon.GetAsync("/api/usuarios/notificaciones/vapid-public-key"),
            HttpStatusCode.Unauthorized,
            cidAnon);
        await AssertProblemDetailsWithCorrelationIdAsync(
            await clientAnon.PostAsJsonAsync("/api/usuarios/notificaciones/suscripciones", new { }),
            HttpStatusCode.Unauthorized,
            cidAnon);
        await AssertProblemDetailsWithCorrelationIdAsync(
            await clientAnon.DeleteAsync($"/api/usuarios/notificaciones/suscripciones/{Guid.NewGuid()}"),
            HttpStatusCode.Unauthorized,
            cidAnon);

        var clientAuth = factory.CreateClient();
        var cid = Guid.NewGuid().ToString();
        clientAuth.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Capataz", userId));
        clientAuth.DefaultRequestHeaders.Add("X-Correlation-ID", cid);

        var respVapid = await clientAuth.GetAsync("/api/usuarios/notificaciones/vapid-public-key");
        Assert.Equal(HttpStatusCode.OK, respVapid.StatusCode);
        var vapidBody = await respVapid.Content.ReadAsStringAsync();
        Assert.DoesNotContain(privateSecret, vapidBody, StringComparison.Ordinal);
        var vapidDto = await respVapid.Content.ReadFromJsonAsync<VapidPublicKeyResponseDto>();
        Assert.Equal("BPu-public-key-visible-227", vapidDto!.PublicKey);

        // Endpoint HTTP no localhost -> 400
        var respHttpInvalido = await clientAuth.PostAsJsonAsync("/api/usuarios/notificaciones/suscripciones", new
        {
            endpoint = "http://push.inseguro.com/abc",
            p256dh = "clave-p256",
            auth = "clave-auth"
        });
        await AssertProblemDetailsWithCorrelationIdAsync(respHttpInvalido, HttpStatusCode.BadRequest, cid);

        // p256dh / auth vacíos -> 400
        var respSinClave = await clientAuth.PostAsJsonAsync("/api/usuarios/notificaciones/suscripciones", new
        {
            endpoint = "https://fcm.googleapis.com/fcm/send/123",
            p256dh = " ",
            auth = "clave-auth"
        });
        await AssertProblemDetailsWithCorrelationIdAsync(respSinClave, HttpStatusCode.BadRequest, cid);

        // Sin navegador explícito -> toma User-Agent recortado a 200 caracteres; http://localhost permitido en dev
        var userAgentLargo = "Mozilla/5.0-" + new string('X', 250);
        clientAuth.DefaultRequestHeaders.UserAgent.Clear();
        clientAuth.DefaultRequestHeaders.Add("User-Agent", userAgentLargo);

        var respCreated = await clientAuth.PostAsJsonAsync("/api/usuarios/notificaciones/suscripciones", new
        {
            endpoint = "http://localhost:8085/push/dev-1",
            p256dh = "clave-p256",
            auth = "clave-auth"
        });
        Assert.Equal(HttpStatusCode.Created, respCreated.StatusCode);
        var subDto = await respCreated.Content.ReadFromJsonAsync<SuscripcionPushResponseDto>();
        Assert.NotNull(subDto);
        Assert.Equal(userId, subDto.UsuarioId);
        Assert.NotNull(subDto.Navegador);
        Assert.Equal(200, subDto.Navegador!.Length);

        foreach (var log in loggerProvider.Mensajes)
        {
            Assert.DoesNotContain(privateSecret, log, StringComparison.Ordinal);
        }

        // En Production se rechaza http://localhost
        using var factoryProd = CreateIsolatedFactory(out _, environmentName: "Production");
        var clientProd = factoryProd.CreateClient();
        var cidProd = Guid.NewGuid().ToString();
        clientProd.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Capataz", userId));
        clientProd.DefaultRequestHeaders.Add("X-Correlation-ID", cidProd);

        var respLocalEnProd = await clientProd.PostAsJsonAsync("/api/usuarios/notificaciones/suscripciones", new
        {
            endpoint = "http://localhost:8085/push/dev-1",
            p256dh = "clave-p256",
            auth = "clave-auth"
        });
        await AssertProblemDetailsWithCorrelationIdAsync(respLocalEnProd, HttpStatusCode.BadRequest, cidProd);
    }

    [Fact]
    public async Task LimiteDe5SuscripcionesYPermisosDeBorrado_ReemplazaMasAntiguaYBloqueaAjenasCon403()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        using var factory = CreateIsolatedFactory(out _, seedAction: db =>
        {
            db.Users.AddRange(
                new AppUser { Id = userA, UserName = "a@obrasync.test", NormalizedUserName = "A@OBRASYNC.TEST", Email = "a@obrasync.test", NormalizedEmail = "A@OBRASYNC.TEST", NombreCompleto = "User A", Activo = true },
                new AppUser { Id = userB, UserName = "b@obrasync.test", NormalizedUserName = "B@OBRASYNC.TEST", Email = "b@obrasync.test", NormalizedEmail = "B@OBRASYNC.TEST", NombreCompleto = "User B", Activo = true });
        });

        var clientA = factory.CreateClient();
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador", userA));

        var clientB = factory.CreateClient();
        var cidB = Guid.NewGuid().ToString();
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador", userB));
        clientB.DefaultRequestHeaders.Add("X-Correlation-ID", cidB);

        var ids = new List<Guid>();
        for (var i = 1; i <= 5; i++)
        {
            var r = await clientA.PostAsJsonAsync("/api/usuarios/notificaciones/suscripciones", new
            {
                endpoint = $"https://push.example.com/device-{i}",
                p256dh = $"p256-{i}",
                auth = $"auth-{i}",
                navegador = $"Chrome-{i}"
            });
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
            var dto = await r.Content.ReadFromJsonAsync<SuscripcionPushResponseDto>();
            ids.Add(dto!.Id);
            await Task.Delay(5);
        }

        // Re-suscribir mismo endpoint #2 -> 200 OK (upsert idempotente, siguen siendo 5)
        var rUpsert = await clientA.PostAsJsonAsync("/api/usuarios/notificaciones/suscripciones", new
        {
            endpoint = "https://push.example.com/device-2",
            p256dh = "p256-2-renovado",
            auth = "auth-2-renovado",
            navegador = "Chrome-2-v2"
        });
        Assert.Equal(HttpStatusCode.OK, rUpsert.StatusCode);
        var dtoUpsert = await rUpsert.Content.ReadFromJsonAsync<SuscripcionPushResponseDto>();
        Assert.Equal(ids[1], dtoUpsert!.Id);

        // Simular que el device-1 tuvo envío reciente -> entonces el más antiguo por (UltimoEnvio ?? FechaAlta) pasa a ser el device-3
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var sub1 = await db.SuscripcionesPush.FirstAsync(s => s.Id == ids[0]);
            sub1.UltimoEnvio = DateTime.UtcNow.AddMinutes(5);
            await db.SaveChangesAsync();
        }

        // Llega una 6.ª suscripción -> reemplaza a device-3 y mantiene exactamente 5 para userA
        var rSexta = await clientA.PostAsJsonAsync("/api/usuarios/notificaciones/suscripciones", new
        {
            endpoint = "https://push.example.com/device-6",
            p256dh = "p256-6",
            auth = "auth-6",
            navegador = "Firefox-6"
        });
        Assert.Equal(HttpStatusCode.Created, rSexta.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var endpointsActivos = await db.SuscripcionesPush
                .Where(s => s.UsuarioId == userA)
                .Select(s => s.Endpoint)
                .ToListAsync();

            Assert.Equal(5, endpointsActivos.Count);
            Assert.DoesNotContain("https://push.example.com/device-3", endpointsActivos);
            Assert.Contains("https://push.example.com/device-1", endpointsActivos);
            Assert.Contains("https://push.example.com/device-6", endpointsActivos);
        }

        // User B intenta borrar suscripción de User A -> 403 Forbidden
        var respAjena = await clientB.DeleteAsync($"/api/usuarios/notificaciones/suscripciones/{ids[0]}");
        await AssertProblemDetailsWithCorrelationIdAsync(respAjena, HttpStatusCode.Forbidden, cidB);

        // Borrar inexistente -> 404 NotFound
        var respInexistente = await clientB.DeleteAsync($"/api/usuarios/notificaciones/suscripciones/{Guid.NewGuid()}");
        await AssertProblemDetailsWithCorrelationIdAsync(respInexistente, HttpStatusCode.NotFound, cidB);

        // User A borra propia por query endpoint -> 204 NoContent, luego 404 si repite
        var respDeleteEndpoint = await clientA.DeleteAsync(
            $"/api/usuarios/notificaciones/suscripciones?endpoint={Uri.EscapeDataString("https://push.example.com/device-6")}");
        Assert.Equal(HttpStatusCode.NoContent, respDeleteEndpoint.StatusCode);

        var respDeleteRepetido = await clientA.DeleteAsync(
            $"/api/usuarios/notificaciones/suscripciones?endpoint={Uri.EscapeDataString("https://push.example.com/device-6")}");
        Assert.Equal(HttpStatusCode.NotFound, respDeleteRepetido.StatusCode);
    }

    [Fact]
    public async Task EnvioPush_Elimina404Y410_AislaErroresEnLote_YActualizaUltimoEnvio()
    {
        var userId = Guid.NewGuid();
        var sub404 = SuscripcionPush.Crear(userId, "https://push.test/404", "p1", "a1", "Chrome");
        var sub410 = SuscripcionPush.Crear(userId, "https://push.test/410", "p2", "a2", "Edge");
        var subError = SuscripcionPush.Crear(userId, "https://push.test/503", "p3", "a3", "Safari");
        var subOk = SuscripcionPush.Crear(userId, "https://push.test/ok", "p4", "a4", "Firefox");

        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase($"DB_Lote_227_{Guid.NewGuid()}")
            .Options;

        await using (var dbSeed = new AuthDbContext(options))
        {
            dbSeed.SuscripcionesPush.AddRange(sub404, sub410, subError, subOk);
            await dbSeed.SaveChangesAsync();
        }

        var fakeSender = Substitute.For<IWebPushSender>();
        fakeSender.EnviarAsync(Arg.Is<SuscripcionPush>(s => s.Id == sub404.Id), Arg.Any<PayloadNotificacionPushDto>(), Arg.Any<CancellationToken>())
            .Returns(new ResultadoEnvioWebPush(EstadoEnvioWebPush.SuscripcionExpirada, 404));
        fakeSender.EnviarAsync(Arg.Is<SuscripcionPush>(s => s.Id == sub410.Id), Arg.Any<PayloadNotificacionPushDto>(), Arg.Any<CancellationToken>())
            .Returns(new ResultadoEnvioWebPush(EstadoEnvioWebPush.SuscripcionExpirada, 410));
        fakeSender.EnviarAsync(Arg.Is<SuscripcionPush>(s => s.Id == subError.Id), Arg.Any<PayloadNotificacionPushDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ResultadoEnvioWebPush>(new InvalidOperationException("Fallo inesperado aislado")));
        fakeSender.EnviarAsync(Arg.Is<SuscripcionPush>(s => s.Id == subOk.Id), Arg.Any<PayloadNotificacionPushDto>(), Arg.Any<CancellationToken>())
            .Returns(new ResultadoEnvioWebPush(EstadoEnvioWebPush.Enviado, 201));

        await using (var db = new AuthDbContext(options))
        {
            var repo = new SuscripcionPushRepository(db);
            var service = new NotificacionPushService(repo, fakeSender, NullLogger<NotificacionPushService>.Instance);

            await service.EnviarAUsuariosAsync(
                new[] { userId },
                new PayloadNotificacionPushDto("Título", "Cuerpo", "/compras/ordenes", "Test", DateTime.UtcNow));

            var restantes = await db.SuscripcionesPush.OrderBy(s => s.Endpoint).ToListAsync();
            Assert.Equal(2, restantes.Count);
            Assert.DoesNotContain(restantes, s => s.Id == sub404.Id || s.Id == sub410.Id);

            var sOk = restantes.Single(s => s.Id == subOk.Id);
            Assert.NotNull(sOk.UltimoEnvio);

            var sErr = restantes.Single(s => s.Id == subError.Id);
            Assert.Null(sErr.UltimoEnvio);
        }
    }

    [Fact]
    public async Task WebPushSenderConPolly_ReintentaTransitorios503YNoReintenta410YNuncaLogueaClaves()
    {
        var (vapidPub, vapidPriv) = GenerarParClavesEcBase64Url();
        var (clientPub, _) = GenerarParClavesEcBase64Url();
        var authSecret = Base64UrlEncode(RandomNumberGenerator.GetBytes(16));

        var suscripcion = SuscripcionPush.Crear(
            Guid.NewGuid(),
            "https://fcm.googleapis.com/fcm/send/dispositivo-polly",
            clientPub,
            authSecret,
            "Chrome");

        var handler = new FakePushHttpHandler();
        handler.RespuestasEnCola.Enqueue(HttpStatusCode.ServiceUnavailable);
        handler.RespuestasEnCola.Enqueue(HttpStatusCode.ServiceUnavailable);
        handler.RespuestasEnCola.Enqueue(HttpStatusCode.Created);

        var loggerProvider = new CapturaLogsPushProvider();
        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(loggerProvider).SetMinimumLevel(LogLevel.Trace));

        using var httpClient = new HttpClient(handler);
        var vapidOpts = Options.Create(new VapidSettings
        {
            PublicKey = vapidPub,
            PrivateKey = vapidPriv,
            Subject = "mailto:push@obrasync.local"
        });

        var sender = new WebPushSender(
            httpClient,
            vapidOpts,
            loggerFactory.CreateLogger<WebPushSender>(),
            esperaBaseRetry: TimeSpan.FromMilliseconds(1),
            maxReintentos: 3,
            usarJitter: false);

        var payload = new PayloadNotificacionPushDto("Prueba", "Mensaje seguro", "/compras/ordenes", "Test", DateTime.UtcNow);
        var resultadoOk = await sender.EnviarAsync(suscripcion, payload);

        Assert.Equal(EstadoEnvioWebPush.Enviado, resultadoOk.Estado);
        Assert.Equal(3, handler.CantidadLlamadas);

        // Ante 410 Gone -> Polly NO debe reintentar (solo 1 llamada más -> 4 en total)
        handler.RespuestasEnCola.Enqueue(HttpStatusCode.Gone);
        var resultado410 = await sender.EnviarAsync(suscripcion, payload);
        Assert.Equal(EstadoEnvioWebPush.SuscripcionExpirada, resultado410.Estado);
        Assert.Equal(410, resultado410.StatusCode);
        Assert.Equal(4, handler.CantidadLlamadas);

        foreach (var msg in loggerProvider.Mensajes)
        {
            Assert.DoesNotContain(vapidPriv, msg, StringComparison.Ordinal);
            Assert.DoesNotContain(clientPub, msg, StringComparison.Ordinal);
            Assert.DoesNotContain(authSecret, msg, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task SeisConsumidoresMassTransit_ResuelvenDestinatariosExactosSinMontosYSonIdempotentes()
    {
        var dbOptions = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase($"DB_Consumers_227_{Guid.NewGuid()}")
            .Options;

        var adminId = Guid.NewGuid();
        var compradorId = Guid.NewGuid();
        var compradorInactivoId = Guid.NewGuid();
        var capatazObraAId = Guid.NewGuid();
        var capatazObraBId = Guid.NewGuid();
        var provDuenoId = Guid.NewGuid();
        var provAjenoId = Guid.NewGuid();
        var obraAId = Guid.NewGuid();
        var obraBId = Guid.NewGuid();

        await using (var dbSeed = new AuthDbContext(dbOptions))
        {
            await SembrarUsuarioConRolAsync(dbSeed, adminId, "admin@t.com", "Ana Admin", UserRoles.Admin, activo: true);
            await SembrarUsuarioConRolAsync(dbSeed, compradorId, "comp@t.com", "Carlos Comprador", UserRoles.Comprador, activo: true);
            await SembrarUsuarioConRolAsync(dbSeed, compradorInactivoId, "inact@t.com", "Inactivo", UserRoles.Comprador, activo: false);
            await SembrarUsuarioConRolAsync(dbSeed, capatazObraAId, "capA@t.com", "Capataz A", UserRoles.Capataz, activo: true);
            await SembrarUsuarioConRolAsync(dbSeed, capatazObraBId, "capB@t.com", "Capataz B", UserRoles.Capataz, activo: true);
            await SembrarUsuarioConRolAsync(dbSeed, provDuenoId, "provA@t.com", "Proveedor A", UserRoles.Proveedor, activo: true);
            await SembrarUsuarioConRolAsync(dbSeed, provAjenoId, "provB@t.com", "Proveedor B", UserRoles.Proveedor, activo: true);

            dbSeed.Obras.AddRange(
                new Obra { Id = obraAId, Nombre = "Obra A", Ubicacion = "CABA", CapatazId = capatazObraAId, Activa = true },
                new Obra { Id = obraBId, Nombre = "Obra B", Ubicacion = "LP", CapatazId = capatazObraBId, Activa = true });

            await dbSeed.SaveChangesAsync();
        }

        var envios = new ConcurrentQueue<(Guid UsuarioId, PayloadNotificacionPushDto Payload)>();
        var fakeSender = Substitute.For<IWebPushSender>();
        fakeSender.EnviarAsync(Arg.Any<SuscripcionPush>(), Arg.Any<PayloadNotificacionPushDto>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var sub = ci.ArgAt<SuscripcionPush>(0);
                var payload = ci.ArgAt<PayloadNotificacionPushDto>(1);
                envios.Enqueue((sub.UsuarioId, payload));
                return Task.FromResult(new ResultadoEnvioWebPush(EstadoEnvioWebPush.Enviado, 201));
            });

        static ConsumeContext<T> CrearCtx<T>(T msg) where T : class
        {
            var ctx = Substitute.For<ConsumeContext<T>>();
            ctx.Message.Returns(msg);
            ctx.CorrelationId.Returns(Guid.NewGuid());
            ctx.CancellationToken.Returns(CancellationToken.None);
            return ctx;
        }

        await using var db = new AuthDbContext(dbOptions);
        var repo = new SuscripcionPushRepository(db);
        var destService = new DestinatariosNotificacionService(db);
        var pushService = new NotificacionPushService(repo, fakeSender, NullLogger<NotificacionPushService>.Instance);

        // 1. RequisicionAprobadaEvent -> Comprador + Admin activos (/compras/licitaciones) y Capataz deduplicado (/obra/requisiciones/{id}/seguimiento)
        var reqConsumer = new RequisicionAprobadaConsumer(db, destService, pushService, NullLogger<RequisicionAprobadaConsumer>.Instance);
        var reqId = Guid.NewGuid();
        var evReq = new RequisicionAprobadaEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow,
            CorrelationId: Guid.NewGuid(),
            RequisicionId: reqId,
            NumeroRequisicion: "REQ-227-01",
            ObraId: obraAId,
            SolicitanteId: capatazObraAId,
            AprobadorId: compradorId,
            Items: new[] { new RequisicionAprobadaItemEvent(Guid.NewGuid(), "Cemento", 10m, "BOLSA") });

        await reqConsumer.Consume(CrearCtx(evReq));
        await reqConsumer.Consume(CrearCtx(evReq)); // duplicado mismo EventId -> no duplica

        var lote1 = DequeueAll(envios);
        Assert.Equal(3, lote1.Count);
        Assert.Contains(lote1, e => e.UsuarioId == adminId && e.Payload.Ruta == "/compras/licitaciones");
        Assert.Contains(lote1, e => e.UsuarioId == compradorId && e.Payload.Ruta == "/compras/licitaciones");
        Assert.Contains(lote1, e => e.UsuarioId == capatazObraAId && e.Payload.Ruta == $"/obra/requisiciones/{reqId}/seguimiento");
        Assert.DoesNotContain(lote1, e => e.UsuarioId == compradorInactivoId || e.UsuarioId == capatazObraBId);

        // 2. OrdenCompraEmitidaEvent -> Proveedor dueño + Capataz obraA, SIN montos ni datos sensibles
        var ocEmitidaConsumer = new OrdenCompraEmitidaConsumer(db, destService, pushService, NullLogger<OrdenCompraEmitidaConsumer>.Instance);
        var evEmitida = new OrdenCompraEmitidaEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow,
            CorrelationId: Guid.NewGuid(),
            OrdenCompraId: Guid.NewGuid(),
            LicitacionId: Guid.NewGuid(),
            RequisicionId: reqId,
            ObraId: obraAId,
            ProveedorId: provDuenoId,
            Etapa: "Fundaciones",
            MontoTotal: 987654.32m,
            Items: new[] { new OrdenCompraItemDto(Guid.NewGuid(), "Hierro", 5m, "BARRA", 197530.86m) });

        await ocEmitidaConsumer.Consume(CrearCtx(evEmitida));
        await ocEmitidaConsumer.Consume(CrearCtx(evEmitida));

        var lote2 = DequeueAll(envios);
        Assert.Equal(2, lote2.Count);
        Assert.Contains(lote2, e => e.UsuarioId == provDuenoId && e.Payload.Ruta == "/proveedores/mis-ordenes");
        Assert.Contains(lote2, e => e.UsuarioId == capatazObraAId && e.Payload.Ruta == $"/obra/requisiciones/{reqId}/seguimiento");
        Assert.All(lote2, e =>
        {
            Assert.DoesNotContain("987654", e.Payload.Titulo + e.Payload.Cuerpo);
            Assert.DoesNotContain("$", e.Payload.Titulo + e.Payload.Cuerpo);
        });

        // 3. OrdenCompraDespachadaEvent -> Capataz obraA (/obra/recepcion) + Compras (/compras/ordenes)
        var ocDespConsumer = new OrdenCompraDespachadaConsumer(db, destService, pushService, NullLogger<OrdenCompraDespachadaConsumer>.Instance);
        var evDesp = new OrdenCompraDespachadaEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow,
            CorrelationId: Guid.NewGuid(),
            OrdenCompraId: evEmitida.OrdenCompraId,
            ObraId: obraAId,
            FechaDespacho: DateTime.UtcNow,
            FechaEstimadaLlegada: DateTime.UtcNow.AddDays(2));

        await ocDespConsumer.Consume(CrearCtx(evDesp));
        await ocDespConsumer.Consume(CrearCtx(evDesp));

        var lote3 = DequeueAll(envios);
        Assert.Equal(3, lote3.Count);
        Assert.Contains(lote3, e => e.UsuarioId == capatazObraAId && e.Payload.Ruta == "/obra/recepcion");
        Assert.Contains(lote3, e => e.UsuarioId == compradorId && e.Payload.Ruta == "/compras/ordenes");
        Assert.Contains(lote3, e => e.UsuarioId == adminId && e.Payload.Ruta == "/compras/ordenes");

        // 4. RecepcionConfirmadaEvent -> Compras (/compras/ordenes); y si llega ProveedorId opcional, también Proveedor
        var recConfConsumer = new RecepcionConfirmadaConsumer(db, destService, pushService, NullLogger<RecepcionConfirmadaConsumer>.Instance);
        var evRecConf = new RecepcionConfirmadaEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow,
            CorrelationId: Guid.NewGuid(),
            RecepcionId: Guid.NewGuid(),
            ObraId: obraAId,
            OrdenCompraId: evEmitida.OrdenCompraId,
            NumeroRemito: "REM-900",
            FechaRecepcion: DateTime.UtcNow,
            Items: new[] { new RecepcionConfirmadaItemEvent(Guid.NewGuid(), 5m, "BARRA") },
            OrdenCompleta: true,
            ProveedorId: null);

        await recConfConsumer.Consume(CrearCtx(evRecConf));
        await recConfConsumer.Consume(CrearCtx(evRecConf));

        var lote4 = DequeueAll(envios);
        Assert.Equal(2, lote4.Count);
        Assert.All(lote4, e =>
        {
            Assert.Contains(e.UsuarioId, new[] { adminId, compradorId });
            Assert.Equal("/compras/ordenes", e.Payload.Ruta);
            Assert.Contains("completa", e.Payload.Cuerpo);
        });

        // 5. RecepcionConProblemaEvent -> Compras + Proveedor dueño, usando solo Motivo (sin Detalle)
        var recProbConsumer = new RecepcionConProblemaConsumer(db, destService, pushService, NullLogger<RecepcionConProblemaConsumer>.Instance);
        var evProb = new RecepcionConProblemaEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow,
            CorrelationId: Guid.NewGuid(),
            OrdenCompraId: evEmitida.OrdenCompraId,
            RecepcionId: Guid.NewGuid(),
            Motivo: "Llegó dañada",
            Detalle: "Detalle interno confidencial 9911",
            CantidadFotos: 1,
            ObraId: obraAId,
            ProveedorId: provDuenoId);

        await recProbConsumer.Consume(CrearCtx(evProb));
        await recProbConsumer.Consume(CrearCtx(evProb));

        var lote5 = DequeueAll(envios);
        Assert.Equal(3, lote5.Count);
        Assert.Contains(lote5, e => e.UsuarioId == adminId && e.Payload.Ruta == "/compras/ordenes");
        Assert.Contains(lote5, e => e.UsuarioId == compradorId && e.Payload.Ruta == "/compras/ordenes");
        Assert.Contains(lote5, e => e.UsuarioId == provDuenoId && e.Payload.Ruta == "/proveedores/mis-ordenes");
        Assert.All(lote5, e =>
        {
            Assert.Contains("Llegó dañada", e.Payload.Cuerpo);
            Assert.DoesNotContain("9911", e.Payload.Titulo + e.Payload.Cuerpo);
        });

        // 6. MensajeChatEnviadoEvent -> Proveedor escribe a Compras (excluye remitente) y Comprador escribe a Proveedor
        var chatConsumer = new MensajeChatEnviadoConsumer(db, destService, pushService, NullLogger<MensajeChatEnviadoConsumer>.Instance);
        var evChatDesdeProv = new MensajeChatEnviadoEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow,
            CorrelationId: Guid.NewGuid(),
            OrdenCompraId: evEmitida.OrdenCompraId,
            Remitente: new RemitenteChatEvent(provDuenoId, "Proveedor A", "Proveedor"),
            Destinatarios: new DestinatariosChatEvent(Array.Empty<Guid>(), new[] { "Comprador", "Admin" }),
            Fragmento: "Sale el flete mañana temprano");

        await chatConsumer.Consume(CrearCtx(evChatDesdeProv));
        await chatConsumer.Consume(CrearCtx(evChatDesdeProv));

        var lote6a = DequeueAll(envios);
        Assert.Equal(2, lote6a.Count);
        Assert.Contains(lote6a, e => e.UsuarioId == adminId && e.Payload.Ruta == "/compras/ordenes" && e.Payload.Cuerpo == "Sale el flete mañana temprano");
        Assert.Contains(lote6a, e => e.UsuarioId == compradorId && e.Payload.Ruta == "/compras/ordenes");

        var evChatDesdeComp = new MensajeChatEnviadoEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow,
            CorrelationId: Guid.NewGuid(),
            OrdenCompraId: evEmitida.OrdenCompraId,
            Remitente: new RemitenteChatEvent(compradorId, "Carlos Comprador", "Comprador"),
            Destinatarios: new DestinatariosChatEvent(new[] { provDuenoId, compradorId }, Array.Empty<string>()),
            Fragmento: "Recibido, gracias");

        await chatConsumer.Consume(CrearCtx(evChatDesdeComp));
        var lote6b = DequeueAll(envios);
        var unico = Assert.Single(lote6b);
        Assert.Equal(provDuenoId, unico.UsuarioId);
        Assert.Equal("/proveedores/mis-ordenes", unico.Payload.Ruta);
        Assert.Equal("Recibido, gracias", unico.Payload.Cuerpo);

        Assert.Equal(7, await db.EventosProcesados.CountAsync());
    }

    [Fact]
    public void MassTransit_UsaPrefijoExclusivoDeUsuarios_EnLosSeisConsumidores()
    {
        using var factory = CreateIsolatedFactory(out _);
        using var scope = factory.Services.CreateScope();
        var formatter = scope.ServiceProvider.GetRequiredService<IEndpointNameFormatter>();

        var colas = new[]
        {
            formatter.Consumer<RequisicionAprobadaConsumer>(),
            formatter.Consumer<OrdenCompraEmitidaConsumer>(),
            formatter.Consumer<OrdenCompraDespachadaConsumer>(),
            formatter.Consumer<RecepcionConfirmadaConsumer>(),
            formatter.Consumer<RecepcionConProblemaConsumer>(),
            formatter.Consumer<MensajeChatEnviadoConsumer>()
        };

        Assert.All(colas, nombre => Assert.StartsWith("usuarios.", nombre, StringComparison.Ordinal));
        Assert.Equal(6, colas.Distinct(StringComparer.Ordinal).Count());
    }

    private static List<(Guid UsuarioId, PayloadNotificacionPushDto Payload)> DequeueAll(
        ConcurrentQueue<(Guid UsuarioId, PayloadNotificacionPushDto Payload)> queue)
    {
        var list = new List<(Guid, PayloadNotificacionPushDto)>();
        while (queue.TryDequeue(out var item))
        {
            list.Add(item);
        }

        return list;
    }

    private sealed class FakePushHttpHandler : HttpMessageHandler
    {
        public ConcurrentQueue<HttpStatusCode> RespuestasEnCola { get; } = new();
        public int CantidadLlamadas { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CantidadLlamadas++;
            var code = RespuestasEnCola.TryDequeue(out var next)
                ? next
                : HttpStatusCode.Created;
            return Task.FromResult(new HttpResponseMessage(code));
        }
    }

    private sealed class CapturaLogsPushProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Mensajes { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturaLogger(Mensajes);

        public void Dispose() { }

        private sealed class CapturaLogger : ILogger
        {
            private readonly ConcurrentQueue<string> _mensajes;

            public CapturaLogger(ConcurrentQueue<string> mensajes) => _mensajes = mensajes;

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                _mensajes.Enqueue(formatter(state, exception));
            }
        }
    }
}
