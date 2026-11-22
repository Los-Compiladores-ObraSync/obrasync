using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using ObraSync.Contracts.Events;
using ObraSync.Licitaciones.Api.Hubs;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Domain.Entities;
using ObraSync.Licitaciones.Infrastructure;
using ObraSync.Licitaciones.Infrastructure.Consumers;
using Xunit;

namespace ObraSync.Licitaciones.Tests;

public class ChatOrdenCompraTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestJwtSecret = "clave-de-pruebas-de-al-menos-32-caracteres";
    private readonly WebApplicationFactory<Program> _baseFactory;

    public ChatOrdenCompraTests(WebApplicationFactory<Program> factory)
    {
        Environment.SetEnvironmentVariable("Jwt__Secret", TestJwtSecret);
        _baseFactory = factory;
    }

    private WebApplicationFactory<Program> CreateIsolatedFactory(
        out string dbName,
        out IPublishEndpoint publishSpy,
        out IChatOrdenNotifier notifierSpy,
        InMemoryLoggerProvider? loggerProvider = null,
        Action<LicitacionesDbContext>? seedAction = null,
        string environmentName = "Development")
    {
        dbName = $"DB_Licit_218_{Guid.NewGuid()}";
        var nombreDb = dbName;
        var pubSpy = Substitute.For<IPublishEndpoint>();
        var chatSpy = Substitute.For<IChatOrdenNotifier>();
        publishSpy = pubSpy;
        notifierSpy = chatSpy;

        return _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environmentName);
            builder.UseSetting("Jwt:Secret", TestJwtSecret);
            builder.UseSetting("RabbitMQ:UseInMemory", "true");

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();

                if (loggerProvider is not null)
                {
                    services.AddSingleton<ILoggerProvider>(loggerProvider);
                }

                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<LicitacionesDbContext>));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                services.RemoveAll<IDbContextOptionsConfiguration<LicitacionesDbContext>>();

                services.AddDbContext<LicitacionesDbContext>(options =>
                {
                    options.UseInMemoryDatabase(nombreDb)
                        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
                });

                services.RemoveAll<IPublishEndpoint>();
                services.AddSingleton(pubSpy);

                services.RemoveAll<IChatOrdenNotifier>();
                services.AddSingleton(chatSpy);

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

    private static (Guid ordenId, Guid licitacionId, Guid obraId, Guid proveedorId) SembrarOrden(
        LicitacionesDbContext db,
        Guid? proveedorId = null,
        string estado = "Emitida",
        bool tieneProblemaAbierto = false,
        string? motivoProblema = null)
    {
        var licId = Guid.NewGuid();
        var obraId = Guid.NewGuid();
        var ordenId = Guid.NewGuid();
        var provId = proveedorId ?? Guid.NewGuid();

        db.Licitaciones.Add(new Licitacion
        {
            LicitacionId = licId,
            RequisicionId = Guid.NewGuid(),
            NumeroRequisicion = $"REQ-218-{Random.Shared.Next(1000, 9999)}",
            ObraId = obraId,
            Estado = EstadoLicitacion.Adjudicada,
            FechaCreacion = DateTime.UtcNow.AddDays(-2),
            FechaLimiteCotizacion = DateTime.UtcNow.AddDays(1)
        });

        db.OrdenesCompra.Add(new OrdenCompra
        {
            OrdenCompraId = ordenId,
            LicitacionId = licId,
            CotizacionId = Guid.NewGuid(),
            ProveedorId = provId,
            Estado = estado,
            FechaEmision = DateTime.UtcNow.AddDays(-1),
            TieneProblemaAbierto = tieneProblemaAbierto,
            MotivoProblemaAbierto = motivoProblema,
            FechaProblemaAbierto = tieneProblemaAbierto ? DateTime.UtcNow.AddHours(-2) : null
        });

        return (ordenId, licId, obraId, provId);
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

    [Fact]
    public async Task EndpointsChatYResolverProblema_ValidanPermisos401Y403ConProblemDetails()
    {
        var provDueno = Guid.NewGuid();
        var provAjeno = Guid.NewGuid();
        Guid ordenId = Guid.Empty;

        using var factory = CreateIsolatedFactory(out _, out var publishSpy, out _, seedAction: db =>
        {
            (ordenId, _, _, _) = SembrarOrden(db, proveedorId: provDueno, tieneProblemaAbierto: true, motivoProblema: "Llegó dañada");
        });

        // 1. Sin token -> 401
        var clientAnon = factory.CreateClient();
        var cidAnon = Guid.NewGuid().ToString();
        clientAnon.DefaultRequestHeaders.Add("X-Correlation-ID", cidAnon);

        await AssertProblemDetailsWithCorrelationIdAsync(
            await clientAnon.GetAsync($"/api/ordenes-compra/{ordenId}/chat/mensajes"),
            HttpStatusCode.Unauthorized,
            cidAnon);
        await AssertProblemDetailsWithCorrelationIdAsync(
            await clientAnon.PostAsJsonAsync($"/api/ordenes-compra/{ordenId}/chat/mensajes", new { texto = "Hola" }),
            HttpStatusCode.Unauthorized,
            cidAnon);
        await AssertProblemDetailsWithCorrelationIdAsync(
            await clientAnon.PostAsync($"/api/ordenes-compra/{ordenId}/chat/marcar-leido", null),
            HttpStatusCode.Unauthorized,
            cidAnon);
        await AssertProblemDetailsWithCorrelationIdAsync(
            await clientAnon.GetAsync("/api/ordenes-compra/chat/no-leidos"),
            HttpStatusCode.Unauthorized,
            cidAnon);
        await AssertProblemDetailsWithCorrelationIdAsync(
            await clientAnon.PostAsync($"/api/ordenes-compra/{ordenId}/problema/resolver", null),
            HttpStatusCode.Unauthorized,
            cidAnon);

        // 2. Capataz -> 403 en todo el chat y en resolver problema
        var clientCapataz = factory.CreateClient();
        var cidCap = Guid.NewGuid().ToString();
        clientCapataz.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Capataz"));
        clientCapataz.DefaultRequestHeaders.Add("X-Correlation-ID", cidCap);

        await AssertProblemDetailsWithCorrelationIdAsync(
            await clientCapataz.GetAsync($"/api/ordenes-compra/{ordenId}/chat/mensajes"),
            HttpStatusCode.Forbidden,
            cidCap);
        await AssertProblemDetailsWithCorrelationIdAsync(
            await clientCapataz.PostAsJsonAsync($"/api/ordenes-compra/{ordenId}/chat/mensajes", new { texto = "Hola" }),
            HttpStatusCode.Forbidden,
            cidCap);
        await AssertProblemDetailsWithCorrelationIdAsync(
            await clientCapataz.PostAsync($"/api/ordenes-compra/{ordenId}/chat/marcar-leido", null),
            HttpStatusCode.Forbidden,
            cidCap);
        await AssertProblemDetailsWithCorrelationIdAsync(
            await clientCapataz.GetAsync("/api/ordenes-compra/chat/no-leidos"),
            HttpStatusCode.Forbidden,
            cidCap);
        await AssertProblemDetailsWithCorrelationIdAsync(
            await clientCapataz.PostAsync($"/api/ordenes-compra/{ordenId}/problema/resolver", null),
            HttpStatusCode.Forbidden,
            cidCap);

        // 3. Otro Proveedor (ajeno) -> 403 al acceder a esa orden
        var clientProvAjeno = factory.CreateClient();
        var cidAjeno = Guid.NewGuid().ToString();
        clientProvAjeno.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", provAjeno));
        clientProvAjeno.DefaultRequestHeaders.Add("X-Correlation-ID", cidAjeno);

        await AssertProblemDetailsWithCorrelationIdAsync(
            await clientProvAjeno.GetAsync($"/api/ordenes-compra/{ordenId}/chat/mensajes"),
            HttpStatusCode.Forbidden,
            cidAjeno);
        await AssertProblemDetailsWithCorrelationIdAsync(
            await clientProvAjeno.PostAsJsonAsync($"/api/ordenes-compra/{ordenId}/chat/mensajes", new { texto = "Intento ajeno" }),
            HttpStatusCode.Forbidden,
            cidAjeno);
        await AssertProblemDetailsWithCorrelationIdAsync(
            await clientProvAjeno.PostAsync($"/api/ordenes-compra/{ordenId}/chat/marcar-leido", null),
            HttpStatusCode.Forbidden,
            cidAjeno);
        await AssertProblemDetailsWithCorrelationIdAsync(
            await clientProvAjeno.PostAsync($"/api/ordenes-compra/{ordenId}/problema/resolver", null),
            HttpStatusCode.Forbidden,
            cidAjeno);

        // 4. Proveedor dueño NO puede resolver problema manualmente -> 403
        var clientProvDueno = factory.CreateClient();
        var cidDueno = Guid.NewGuid().ToString();
        clientProvDueno.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", provDueno));
        clientProvDueno.DefaultRequestHeaders.Add("X-Correlation-ID", cidDueno);

        await AssertProblemDetailsWithCorrelationIdAsync(
            await clientProvDueno.PostAsync($"/api/ordenes-compra/{ordenId}/problema/resolver", null),
            HttpStatusCode.Forbidden,
            cidDueno);

        await publishSpy.DidNotReceive().Publish<MensajeChatEnviadoEvent>(Arg.Any<MensajeChatEnviadoEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnviarMensajeHumano_ValidaLongitud_PublicaMensajeChatEnviadoEventSegunRemitente_YNotificaSignalR()
    {
        var compradorId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var provId = Guid.NewGuid();
        Guid ordenId = Guid.Empty;

        using var factory = CreateIsolatedFactory(out _, out var publishSpy, out var notifierSpy, seedAction: db =>
        {
            (ordenId, _, _, _) = SembrarOrden(db, proveedorId: provId);
        });

        var clientComprador = factory.CreateClient();
        var cidComp = Guid.NewGuid();
        clientComprador.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador", compradorId, "Lucía Compras"));
        clientComprador.DefaultRequestHeaders.Add("X-Correlation-ID", cidComp.ToString());

        // 1. Validación 400 para vacío, espacios, > 1000 caracteres y orden inexistente 404
        var respVacio = await clientComprador.PostAsJsonAsync($"/api/ordenes-compra/{ordenId}/chat/mensajes", new { texto = "   " });
        await AssertProblemDetailsWithCorrelationIdAsync(respVacio, HttpStatusCode.BadRequest, cidComp.ToString());

        var respLargo = await clientComprador.PostAsJsonAsync(
            $"/api/ordenes-compra/{ordenId}/chat/mensajes",
            new { texto = new string('x', 1001) });
        await AssertProblemDetailsWithCorrelationIdAsync(respLargo, HttpStatusCode.BadRequest, cidComp.ToString());

        var respInexistente = await clientComprador.PostAsJsonAsync(
            $"/api/ordenes-compra/{Guid.NewGuid()}/chat/mensajes",
            new { texto = "Hola" });
        await AssertProblemDetailsWithCorrelationIdAsync(respInexistente, HttpStatusCode.NotFound, cidComp.ToString());

        // 2. Comprador envía mensaje largo (> 120 chars) -> 201 Created, recorta fragmento a 120 con "…", destinatario=[provId]
        var textoComprador = new string('A', 150);
        var respComp = await clientComprador.PostAsJsonAsync($"/api/ordenes-compra/{ordenId}/chat/mensajes", new { texto = textoComprador });
        Assert.Equal(HttpStatusCode.Created, respComp.StatusCode);

        var dtoComp = await respComp.Content.ReadFromJsonAsync<MensajeChatOrdenDto>();
        Assert.NotNull(dtoComp);
        Assert.Equal(ordenId, dtoComp.OrdenCompraId);
        Assert.Equal(compradorId, dtoComp.AutorId);
        Assert.Equal("Lucía Compras", dtoComp.AutorNombre);
        Assert.Equal("Comprador", dtoComp.AutorRol);
        Assert.False(dtoComp.EsSistema);
        Assert.Equal(textoComprador, dtoComp.Texto);

        await publishSpy.Received(1).Publish<MensajeChatEnviadoEvent>(
            Arg.Is<MensajeChatEnviadoEvent>(e =>
                e.OrdenCompraId == ordenId &&
                e.CorrelationId == cidComp &&
                e.Remitente.UsuarioId == compradorId &&
                e.Remitente.Nombre == "Lucía Compras" &&
                e.Remitente.Rol == "Comprador" &&
                e.Destinatarios.UsuarioIds.SequenceEqual(new[] { provId }) &&
                e.Destinatarios.Roles.Count == 0 &&
                e.Fragmento.Length == 120 &&
                e.Fragmento.EndsWith("…")),
            Arg.Any<CancellationToken>());

        await notifierSpy.Received(1).NotificarMensajeAsync(
            Arg.Is<MensajeChatOrdenDto>(m => m.Id == dtoComp.Id),
            Arg.Any<CancellationToken>());

        // 3. Admin envía mensaje -> destinatario=[provId], roles=[]
        var clientAdmin = factory.CreateClient();
        clientAdmin.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Admin", adminId, "Carlos Admin"));
        var respAdmin = await clientAdmin.PostAsJsonAsync(
            $"/api/licitaciones/ordenes-compra/{ordenId}/chat/mensajes",
            new { texto = "Revisado por administración" });
        Assert.Equal(HttpStatusCode.Created, respAdmin.StatusCode);

        await publishSpy.Received(1).Publish<MensajeChatEnviadoEvent>(
            Arg.Is<MensajeChatEnviadoEvent>(e =>
                e.Remitente.UsuarioId == adminId &&
                e.Remitente.Rol == "Admin" &&
                e.Destinatarios.UsuarioIds.SequenceEqual(new[] { provId }) &&
                e.Destinatarios.Roles.Count == 0 &&
                e.Fragmento == "Revisado por administración"),
            Arg.Any<CancellationToken>());

        // 4. Proveedor titular envía mensaje -> usuarioIds=[], roles=["Comprador", "Admin"]
        var clientProv = factory.CreateClient();
        var cidProv = Guid.NewGuid();
        clientProv.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", provId, "Corralón Norte"));
        clientProv.DefaultRequestHeaders.Add("X-Correlation-ID", cidProv.ToString());

        var respProv = await clientProv.PostAsJsonAsync(
            $"/api/ordenes-compra/{ordenId}/chat/mensajes",
            new { texto = "  Sale el camión hoy a las 14 hs  " });
        Assert.Equal(HttpStatusCode.Created, respProv.StatusCode);

        var dtoProv = await respProv.Content.ReadFromJsonAsync<MensajeChatOrdenDto>();
        Assert.NotNull(dtoProv);
        Assert.Equal("Sale el camión hoy a las 14 hs", dtoProv.Texto);
        Assert.Equal("Proveedor", dtoProv.AutorRol);

        await publishSpy.Received(1).Publish<MensajeChatEnviadoEvent>(
            Arg.Is<MensajeChatEnviadoEvent>(e =>
                e.OrdenCompraId == ordenId &&
                e.CorrelationId == cidProv &&
                e.Remitente.UsuarioId == provId &&
                e.Remitente.Nombre == "Corralón Norte" &&
                e.Remitente.Rol == "Proveedor" &&
                e.Destinatarios.UsuarioIds.Count == 0 &&
                e.Destinatarios.Roles.SequenceEqual(new[] { "Comprador", "Admin" }) &&
                e.Fragmento == "Sale el camión hoy a las 14 hs"),
            Arg.Any<CancellationToken>());

        // 5. Paginación cronológica en GET mensajes (página 1 de 2 y página 2 de 2)
        var respPag1 = await clientProv.GetAsync($"/api/ordenes-compra/{ordenId}/chat/mensajes?pagina=1&tamano=2");
        Assert.Equal(HttpStatusCode.OK, respPag1.StatusCode);
        Assert.Equal("3", respPag1.Headers.GetValues("X-Total-Count").Single());
        Assert.Equal("1", respPag1.Headers.GetValues("X-Page").Single());
        Assert.Equal("2", respPag1.Headers.GetValues("X-Page-Size").Single());

        var pagina1 = await respPag1.Content.ReadFromJsonAsync<List<MensajeChatOrdenDto>>();
        Assert.NotNull(pagina1);
        Assert.Equal(2, pagina1.Count);
        Assert.Equal("Comprador", pagina1[0].AutorRol);
        Assert.Equal("Admin", pagina1[1].AutorRol);

        var pagina2 = await clientComprador.GetFromJsonAsync<List<MensajeChatOrdenDto>>(
            $"/api/ordenes-compra/{ordenId}/chat/mensajes?pagina=2&tamano=2");
        Assert.NotNull(pagina2);
        var ultimo = Assert.Single(pagina2);
        Assert.Equal("Proveedor", ultimo.AutorRol);
    }

    [Fact]
    public async Task MensajesAutomaticosDespachoRecepcionYProblema_SonIdempotentesYNuncaPublicanMensajeChatEnviadoEvent()
    {
        var provId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        Guid ordenId = Guid.Empty;

        using var factory = CreateIsolatedFactory(out var dbName, out var publishSpy, out var notifierSpy, seedAction: db =>
        {
            (ordenId, _, _, _) = SembrarOrden(db, proveedorId: provId, estado: "Emitida");
        });

        // 1. Despachar la orden (#216) -> inserta 1 mensaje automático y notifica SignalR, sin MensajeChatEnviadoEvent
        var clientProv = factory.CreateClient();
        clientProv.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", provId));

        var respDesp1 = await clientProv.PostAsync($"/api/ordenes-compra/{ordenId}/despachar", null);
        Assert.Equal(HttpStatusCode.OK, respDesp1.StatusCode);

        // Reintento idempotente de despacho -> no duplica mensaje del sistema
        var respDesp2 = await clientProv.PostAsync($"/api/ordenes-compra/{ordenId}/despachar", null);
        Assert.Equal(HttpStatusCode.OK, respDesp2.StatusCode);

        await notifierSpy.Received(1).NotificarMensajeAsync(
            Arg.Is<MensajeChatOrdenDto>(m =>
                m.OrdenCompraId == ordenId &&
                m.EsSistema &&
                m.AutorId == null &&
                m.AutorRol == "Sistema" &&
                m.Texto == "El Proveedor despachó la orden"),
            Arg.Any<CancellationToken>());

        await publishSpy.DidNotReceive().Publish<MensajeChatEnviadoEvent>(Arg.Any<MensajeChatEnviadoEvent>(), Arg.Any<CancellationToken>());

        // 2. Consumir RecepcionConProblemaEvent (#217 / #218) + reentrega mismo EventId
        var eventoProblema = new RecepcionConProblemaEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow,
            CorrelationId: Guid.NewGuid(),
            OrdenCompraId: ordenId,
            RecepcionId: Guid.NewGuid(),
            Motivo: "Llegó dañada",
            Detalle: "Cemento húmedo",
            CantidadFotos: 2);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
            var consumerProblema = new RecepcionConProblemaConsumer(
                db,
                NullLogger<RecepcionConProblemaConsumer>.Instance,
                notifierSpy);

            var ctx1 = Substitute.For<ConsumeContext<RecepcionConProblemaEvent>>();
            ctx1.Message.Returns(eventoProblema);
            await consumerProblema.Consume(ctx1);

            // Reentrega idempotente mismo EventId
            await consumerProblema.Consume(ctx1);

            // Orden inexistente no lanza excepción ni guarda evento
            var ctxInexistente = Substitute.For<ConsumeContext<RecepcionConProblemaEvent>>();
            ctxInexistente.Message.Returns(eventoProblema with { EventId = Guid.NewGuid(), OrdenCompraId = Guid.NewGuid() });
            await consumerProblema.Consume(ctxInexistente);
        }

        await notifierSpy.Received(1).NotificarMensajeAsync(
            Arg.Is<MensajeChatOrdenDto>(m =>
                m.OrdenCompraId == ordenId &&
                m.EsSistema &&
                m.Texto == "El Capataz registró un problema: Llegó dañada"),
            Arg.Any<CancellationToken>());

        await notifierSpy.Received(1).NotificarEstadoProblemaAsync(
            Arg.Is<ProblemaOrdenEstadoDto>(p =>
                p.OrdenCompraId == ordenId &&
                p.TieneProblemaAbierto &&
                p.MotivoProblemaAbierto == "Llegó dañada"),
            Arg.Any<CancellationToken>());

        // 3. Recepción Rechazada NO cierra el problema; Recepción parcial válida SÍ lo cierra
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
            var consumerConf = new RecepcionConfirmadaConsumer(
                db,
                NullLogger<RecepcionConfirmadaConsumer>.Instance,
                chatOrdenRepository: null,
                chatOrdenNotifier: notifierSpy);

            var ctxRechazada = Substitute.For<ConsumeContext<RecepcionConfirmadaEvent>>();
            ctxRechazada.Message.Returns(new RecepcionConfirmadaEvent(
                EventId: Guid.NewGuid(),
                OccurredAt: DateTime.UtcNow,
                CorrelationId: Guid.NewGuid(),
                RecepcionId: Guid.NewGuid(),
                ObraId: Guid.NewGuid(),
                OrdenCompraId: ordenId,
                NumeroRemito: "REM-RECH",
                FechaRecepcion: DateTime.UtcNow,
                Items: Array.Empty<RecepcionConfirmadaItemEvent>(),
                OrdenCompleta: false,
                Estado: "Rechazada"));
            await consumerConf.Consume(ctxRechazada);

            var ordenTrasRechazo = await db.OrdenesCompra.AsNoTracking().FirstAsync(x => x.OrdenCompraId == ordenId);
            Assert.True(ordenTrasRechazo.TieneProblemaAbierto);

            var ctxParcial = Substitute.For<ConsumeContext<RecepcionConfirmadaEvent>>();
            ctxParcial.Message.Returns(new RecepcionConfirmadaEvent(
                EventId: Guid.NewGuid(),
                OccurredAt: DateTime.UtcNow,
                CorrelationId: Guid.NewGuid(),
                RecepcionId: Guid.NewGuid(),
                ObraId: Guid.NewGuid(),
                OrdenCompraId: ordenId,
                NumeroRemito: "REM-PARCIAL",
                FechaRecepcion: DateTime.UtcNow,
                Items: Array.Empty<RecepcionConfirmadaItemEvent>(),
                OrdenCompleta: false,
                Estado: "Confirmada"));
            await consumerConf.Consume(ctxParcial);

            var ordenTrasParcial = await db.OrdenesCompra.AsNoTracking().FirstAsync(x => x.OrdenCompraId == ordenId);
            Assert.False(ordenTrasParcial.TieneProblemaAbierto);
            Assert.NotNull(ordenTrasParcial.ProblemaResueltoEn);
            Assert.Null(ordenTrasParcial.ProblemaResueltoPor);
        }

        // 4. Segundo problema + resolución manual por Admin (sin agregar mensaje extra al chat)
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
            var consumerProblema = new RecepcionConProblemaConsumer(
                db,
                NullLogger<RecepcionConProblemaConsumer>.Instance,
                notifierSpy);
            var ctx2 = Substitute.For<ConsumeContext<RecepcionConProblemaEvent>>();
            ctx2.Message.Returns(eventoProblema with { EventId = Guid.NewGuid(), Motivo = "Llegó incompleta" });
            await consumerProblema.Consume(ctx2);
        }

        var clientAdmin = factory.CreateClient();
        clientAdmin.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Admin", adminId));

        var respResolver1 = await clientAdmin.PostAsync($"/api/ordenes-compra/{ordenId}/problema/resolver", null);
        Assert.Equal(HttpStatusCode.OK, respResolver1.StatusCode);
        var dtoResolver1 = await respResolver1.Content.ReadFromJsonAsync<ProblemaOrdenEstadoDto>();
        Assert.NotNull(dtoResolver1);
        Assert.False(dtoResolver1.TieneProblemaAbierto);
        Assert.Equal(adminId, dtoResolver1.ProblemaResueltoPor);

        // Idempotencia al volver a resolver
        var respResolver2 = await clientAdmin.PostAsync($"/api/ordenes-compra/{ordenId}/problema/resolver", null);
        Assert.Equal(HttpStatusCode.OK, respResolver2.StatusCode);
        var dtoResolver2 = await respResolver2.Content.ReadFromJsonAsync<ProblemaOrdenEstadoDto>();
        Assert.Equal(dtoResolver1.ProblemaResueltoEn, dtoResolver2!.ProblemaResueltoEn);

        // 5. RecepcionConfirmadaEvent con OrdenCompleta = true -> "Llegó todo el pedido" (idempotente)
        var eventCompleta = new RecepcionConfirmadaEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow,
            CorrelationId: Guid.NewGuid(),
            RecepcionId: Guid.NewGuid(),
            ObraId: Guid.NewGuid(),
            OrdenCompraId: ordenId,
            NumeroRemito: "REM-COMP",
            FechaRecepcion: DateTime.UtcNow,
            Items: Array.Empty<RecepcionConfirmadaItemEvent>(),
            OrdenCompleta: true,
            Estado: "Confirmada");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
            var consumerConf = new RecepcionConfirmadaConsumer(
                db,
                NullLogger<RecepcionConfirmadaConsumer>.Instance,
                chatOrdenRepository: null,
                chatOrdenNotifier: notifierSpy);

            var ctxComp = Substitute.For<ConsumeContext<RecepcionConfirmadaEvent>>();
            ctxComp.Message.Returns(eventCompleta);
            await consumerConf.Consume(ctxComp);
            await consumerConf.Consume(ctxComp);
        }

        // Comprobar los 4 mensajes en BD (1 despacho + 2 problemas distintos + 1 pedido completo) y CERO MensajeChatEnviadoEvent
        var mensajes = await clientProv.GetFromJsonAsync<List<MensajeChatOrdenDto>>($"/api/ordenes-compra/{ordenId}/chat/mensajes");
        Assert.NotNull(mensajes);
        Assert.Equal(4, mensajes.Count);
        Assert.Equal("El Proveedor despachó la orden", mensajes[0].Texto);
        Assert.Equal("El Capataz registró un problema: Llegó dañada", mensajes[1].Texto);
        Assert.Equal("El Capataz registró un problema: Llegó incompleta", mensajes[2].Texto);
        Assert.Equal("Llegó todo el pedido", mensajes[3].Texto);
        Assert.All(mensajes, m => Assert.True(m.EsSistema));

        await publishSpy.DidNotReceive().Publish<MensajeChatEnviadoEvent>(Arg.Any<MensajeChatEnviadoEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoLeidosYMarcarLeido_FiltranMensajesPropiosYSistemaPorUsuarioYRol()
    {
        var compradorId = Guid.NewGuid();
        var prov1Id = Guid.NewGuid();
        var prov2Id = Guid.NewGuid();
        Guid ordenProv1 = Guid.Empty;
        Guid ordenProv2 = Guid.Empty;

        using var factory = CreateIsolatedFactory(out _, out _, out var notifierSpy, seedAction: db =>
        {
            (ordenProv1, _, _, _) = SembrarOrden(db, proveedorId: prov1Id);
            (ordenProv2, _, _, _) = SembrarOrden(db, proveedorId: prov2Id);
        });

        var clientComprador = factory.CreateClient();
        clientComprador.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador", compradorId));

        var clientProv1 = factory.CreateClient();
        clientProv1.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", prov1Id));

        // Proveedor 1 despacha orden 1 (mensaje sistema -> no leído para ambos inicialmente),
        // y Proveedor 1 envía mensaje humano (no se cuenta como no leído para Prov1, sí para Comprador).
        Assert.Equal(HttpStatusCode.OK, (await clientProv1.PostAsync($"/api/ordenes-compra/{ordenProv1}/despachar", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await clientProv1.PostAsJsonAsync($"/api/ordenes-compra/{ordenProv1}/chat/mensajes", new { texto = "Mensaje de Prov1" })).StatusCode);

        // Comprador envía mensaje humano en orden 2 (para prov2). No cuenta como no leído para Comprador, ni debe aparecer a Prov1.
        Assert.Equal(HttpStatusCode.Created, (await clientComprador.PostAsJsonAsync($"/api/ordenes-compra/{ordenProv2}/chat/mensajes", new { texto = "Mensaje a Prov2" })).StatusCode);

        // Antes de marcar leído:
        // - Prov1 ve 1 no leído en orden 1 (solo el mensaje del sistema; NO su propio mensaje ni nada de orden 2)
        var noLeidosProv1 = await clientProv1.GetFromJsonAsync<ResumenNoLeidosChatDto>("/api/ordenes-compra/chat/no-leidos");
        Assert.NotNull(noLeidosProv1);
        Assert.Equal(1, noLeidosProv1.TotalNoLeidos);
        var itemNoLeidoProv1 = Assert.Single(noLeidosProv1.Ordenes);
        Assert.Equal(ordenProv1, itemNoLeidoProv1.OrdenCompraId);
        Assert.Equal(1, itemNoLeidoProv1.MensajesNoLeidos);

        // - Comprador ve 2 no leídos en orden 1 (sistema + Prov1) y 0 en orden 2 (fue suyo)
        var noLeidosComp = await clientComprador.GetFromJsonAsync<ResumenNoLeidosChatDto>("/api/ordenes-compra/chat/no-leidos");
        Assert.NotNull(noLeidosComp);
        Assert.Equal(2, noLeidosComp.TotalNoLeidos);
        var itemNoLeidoComp = Assert.Single(noLeidosComp.Ordenes);
        Assert.Equal(ordenProv1, itemNoLeidoComp.OrdenCompraId);
        Assert.Equal(2, itemNoLeidoComp.MensajesNoLeidos);

        // - Listado GET /api/ordenes-compra expone mensajesNoLeidos por orden
        var ordenesComp = await clientComprador.GetFromJsonAsync<List<OrdenCompraResponseDto>>("/api/ordenes-compra");
        Assert.NotNull(ordenesComp);
        Assert.Equal(2, ordenesComp.Single(o => o.Id == ordenProv1).MensajesNoLeidos);
        Assert.Equal(0, ordenesComp.Single(o => o.Id == ordenProv2).MensajesNoLeidos);

        // Comprador marca leído orden 1 -> notifica LecturaActualizada y baja a 0
        var respLeido = await clientComprador.PostAsync($"/api/ordenes-compra/{ordenProv1}/chat/marcar-leido", null);
        Assert.Equal(HttpStatusCode.OK, respLeido.StatusCode);
        var dtoLeido = await respLeido.Content.ReadFromJsonAsync<LecturaChatResponseDto>();
        Assert.NotNull(dtoLeido);
        Assert.Equal(ordenProv1, dtoLeido.OrdenCompraId);
        Assert.Equal(compradorId, dtoLeido.UsuarioId);

        await notifierSpy.Received(1).NotificarLecturaAsync(
            Arg.Is<LecturaChatResponseDto>(l => l.OrdenCompraId == ordenProv1 && l.UsuarioId == compradorId),
            Arg.Any<CancellationToken>());

        var noLeidosCompDespues = await clientComprador.GetFromJsonAsync<ResumenNoLeidosChatDto>("/api/ordenes-compra/chat/no-leidos");
        Assert.NotNull(noLeidosCompDespues);
        Assert.Equal(0, noLeidosCompDespues.TotalNoLeidos);
        Assert.Empty(noLeidosCompDespues.Ordenes);

        // Prov1 manda otro mensaje -> Comprador vuelve a tener exactamente 1 no leído
        await Task.Delay(5);
        Assert.Equal(HttpStatusCode.Created, (await clientProv1.PostAsJsonAsync($"/api/ordenes-compra/{ordenProv1}/chat/mensajes", new { texto = "Nuevo aviso" })).StatusCode);

        var noLeidosCompFinal = await clientComprador.GetFromJsonAsync<ResumenNoLeidosChatDto>("/api/ordenes-compra/chat/no-leidos");
        Assert.NotNull(noLeidosCompFinal);
        Assert.Equal(1, noLeidosCompFinal.TotalNoLeidos);
    }

    [Fact]
    public async Task SignalRHub_NegotiateYAccessToken_NoFiltraTokenEnLogsConConfiguracionReal()
    {
        var loggerProvider = new InMemoryLoggerProvider();
        using var factory = CreateIsolatedFactory(out _, out _, out _, loggerProvider: loggerProvider, environmentName: "Development");

        // Verificar que la configuración real de Logging de la app fija Microsoft.AspNetCore.Hosting.Diagnostics en Warning
        var config = factory.Services.GetRequiredService<IConfiguration>();
        Assert.Equal("Warning", config["Logging:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics"]);
        Assert.Equal("Warning", config["Logging:LogLevel:Microsoft.AspNetCore"]);

        var client = factory.CreateClient();
        var cid = Guid.NewGuid().ToString();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", cid);

        // 1. Negotiate sin token -> 401 ProblemDetails
        var respSinToken = await client.PostAsync("/api/ordenes-compra/chat-hub/negotiate?negotiateVersion=1", null);
        await AssertProblemDetailsWithCorrelationIdAsync(respSinToken, HttpStatusCode.Unauthorized, cid);

        // 2. Negotiate con ?access_token= de Capataz -> 403 ProblemDetails
        var tokenCapataz = GenerarTokenJwt("Capataz");
        var respCapataz = await client.PostAsync(
            $"/api/ordenes-compra/chat-hub/negotiate?negotiateVersion=1&access_token={Uri.EscapeDataString(tokenCapataz)}",
            null);
        await AssertProblemDetailsWithCorrelationIdAsync(respCapataz, HttpStatusCode.Forbidden, cid);

        // 3. Negotiate con ?access_token= inválido -> 401
        var tokenInvalido = "jwt-invalido-marca-secreta-218.cuerpo.firma";
        var respInvalido = await client.PostAsync(
            $"/api/ordenes-compra/chat-hub/negotiate?negotiateVersion=1&access_token={Uri.EscapeDataString(tokenInvalido)}",
            null);
        Assert.Equal(HttpStatusCode.Unauthorized, respInvalido.StatusCode);

        // 4. Negotiate con ?access_token= de Comprador -> 200 OK
        var tokenComprador = GenerarTokenJwt("Comprador");
        var respValido = await client.PostAsync(
            $"/api/ordenes-compra/chat-hub/negotiate?negotiateVersion=1&access_token={Uri.EscapeDataString(tokenComprador)}",
            null);
        Assert.Equal(HttpStatusCode.OK, respValido.StatusCode);

        var negotiateJson = await respValido.Content.ReadAsStringAsync();
        using var negotiateDoc = JsonDocument.Parse(negotiateJson);
        Assert.False(string.IsNullOrWhiteSpace(negotiateDoc.RootElement.GetProperty("connectionId").GetString()));

        // 5. Verificar Swagger sigue vivo con 200 OK
        var respSwagger = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, respSwagger.StatusCode);

        // 6. Asegurar que NINGUNA entrada ni scope de log contiene los tokens ni "access_token="
        foreach (var entry in loggerProvider.Entries)
        {
            Assert.DoesNotContain(tokenCapataz, entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(tokenComprador, entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(tokenInvalido, entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("access_token", entry.Message, StringComparison.OrdinalIgnoreCase);

            foreach (var scope in entry.Scopes)
            {
                Assert.DoesNotContain(tokenCapataz, scope, StringComparison.Ordinal);
                Assert.DoesNotContain(tokenComprador, scope, StringComparison.Ordinal);
                Assert.DoesNotContain(tokenInvalido, scope, StringComparison.Ordinal);
                Assert.DoesNotContain("access_token", scope, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public async Task SignalRHub_UnirseYAbandonarGrupo_ValidaPertenenciaRolesYExistencia()
    {
        var provDueno = Guid.NewGuid();
        var provAjeno = Guid.NewGuid();
        var ordenId = Guid.NewGuid();

        var repo = Substitute.For<IOrdenCompraRepository>();
        repo.GetByIdAsync(ordenId, Arg.Any<CancellationToken>()).Returns(new OrdenCompra
        {
            OrdenCompraId = ordenId,
            LicitacionId = Guid.NewGuid(),
            CotizacionId = Guid.NewGuid(),
            ProveedorId = provDueno,
            Estado = "Emitida"
        });
        repo.GetByIdAsync(Arg.Is<Guid>(id => id != ordenId), Arg.Any<CancellationToken>()).Returns((OrdenCompra?)null);

        static HubCallerContext CrearContexto(string rol, Guid? sub = null, string connId = "conn-1")
        {
            var ctx = Substitute.For<HubCallerContext>();
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("sub", (sub ?? Guid.NewGuid()).ToString()),
                new Claim("role", rol),
                new Claim("name", $"User {rol}")
            }, "Bearer", "name", "role"));

            ctx.User.Returns(user);
            ctx.ConnectionId.Returns(connId);
            ctx.ConnectionAborted.Returns(CancellationToken.None);
            return ctx;
        }

        var grupoEsperado = $"orden-{ordenId:D}";

        // 1. Comprador, Admin y Proveedor dueño pueden unirse y salir
        foreach (var (rol, sub) in new[]
        {
            ("Comprador", Guid.NewGuid()),
            ("Admin", Guid.NewGuid()),
            ("Proveedor", provDueno)
        })
        {
            var groups = Substitute.For<IGroupManager>();
            var hub = new ChatOrdenHub(repo)
            {
                Context = CrearContexto(rol, sub, $"conn-{rol}"),
                Groups = groups
            };

            await hub.UnirseAOrden(ordenId);
            await groups.Received(1).AddToGroupAsync($"conn-{rol}", grupoEsperado, Arg.Any<CancellationToken>());

            await hub.SalirDeOrden(ordenId);
            await groups.Received(1).RemoveFromGroupAsync($"conn-{rol}", grupoEsperado, Arg.Any<CancellationToken>());
        }

        // 2. Otro Proveedor, Capataz u orden inexistente reciben HubException sin unirse al grupo
        foreach (var (rol, sub, targetOrden) in new[]
        {
            ("Proveedor", provAjeno, ordenId),
            ("Capataz", Guid.NewGuid(), ordenId),
            ("Comprador", Guid.NewGuid(), Guid.NewGuid())
        })
        {
            var groups = Substitute.For<IGroupManager>();
            var hub = new ChatOrdenHub(repo)
            {
                Context = CrearContexto(rol, sub, $"conn-deny-{rol}"),
                Groups = groups
            };

            await Assert.ThrowsAsync<HubException>(() => hub.UnirseAOrden(targetOrden));
            await groups.DidNotReceiveWithAnyArgs().AddToGroupAsync(default!, default!, default);
        }
    }

    [Fact]
    public async Task SignalRChatOrdenNotifier_EnviaEventosConNombresExactosAlGrupoDeLaOrden()
    {
        var ordenId = Guid.NewGuid();
        var grupoEsperado = $"orden-{ordenId:D}";

        var proxy = Substitute.For<IClientProxy>();
        var clients = Substitute.For<IHubClients>();
        clients.Group(grupoEsperado).Returns(proxy);

        var hubContext = Substitute.For<IHubContext<ChatOrdenHub>>();
        hubContext.Clients.Returns(clients);

        var notifier = new SignalRChatOrdenNotifier(hubContext);

        var mensaje = new MensajeChatOrdenDto(
            Guid.NewGuid(), ordenId, Guid.NewGuid(), "Ana", "Comprador", false, "Hola", DateTime.UtcNow);
        var lectura = new LecturaChatResponseDto(ordenId, Guid.NewGuid(), DateTime.UtcNow);
        var problema = new ProblemaOrdenEstadoDto(ordenId, false, "Llegó dañada", DateTime.UtcNow.AddHours(-1), DateTime.UtcNow, Guid.NewGuid());

        await notifier.NotificarMensajeAsync(mensaje);
        await notifier.NotificarLecturaAsync(lectura);
        await notifier.NotificarEstadoProblemaAsync(problema);

        await proxy.Received(1).SendCoreAsync(
            "MensajeRecibido",
            Arg.Is<object?[]>(args => args.Length == 1 && Equals(args[0], mensaje)),
            Arg.Any<CancellationToken>());

        await proxy.Received(1).SendCoreAsync(
            "LecturaActualizada",
            Arg.Is<object?[]>(args => args.Length == 1 && Equals(args[0], lectura)),
            Arg.Any<CancellationToken>());

        await proxy.Received(1).SendCoreAsync(
            "ProblemaEstadoActualizado",
            Arg.Is<object?[]>(args => args.Length == 1 && Equals(args[0], problema)),
            Arg.Any<CancellationToken>());
    }
}
