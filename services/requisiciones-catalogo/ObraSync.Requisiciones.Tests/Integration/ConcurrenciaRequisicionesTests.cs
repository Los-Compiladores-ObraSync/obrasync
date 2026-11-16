using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using ObraSync.Contracts.Events;
using ObraSync.Requisiciones.Application.DTOs;
using ObraSync.Requisiciones.Application.Interfaces;
using ObraSync.Requisiciones.Application.UseCases.Requisiciones.Commands;
using ObraSync.Requisiciones.Application.UseCases.Requisiciones.Handlers;
using ObraSync.Requisiciones.Domain.Constants;
using ObraSync.Requisiciones.Domain.Entities;
using ObraSync.Requisiciones.Infrastructure.Messaging;
using ObraSync.Requisiciones.Infrastructure.Persistence;
using ObraSync.Requisiciones.Infrastructure.Persistence.Repositories;
using Xunit;

namespace ObraSync.Requisiciones.Tests.Integration
{
    public class ConcurrenciaRequisicionesTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private const string TestJwtSecret = "ClaveDePruebaExclusivaParaIntegracionConLongitudMinima32Bytes!";
        private static readonly Guid CementoSeedId = Guid.Parse("11111111-1111-1111-1111-111111110001");

        private readonly WebApplicationFactory<Program> _baseFactory;

        public ConcurrenciaRequisicionesTests(WebApplicationFactory<Program> factory)
        {
            _baseFactory = factory;
        }

        private WebApplicationFactory<Program> CreateIsolatedFactory(Action<IServiceCollection>? customServices = null)
        {
            var dbName = $"DB_Req_Concurrencia_{Guid.NewGuid()}";

            return _baseFactory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Jwt:Secret", TestJwtSecret);
                builder.UseSetting("ConnectionStrings:DefaultConnection", "InMemoryTestDb");
                builder.UseSetting("RabbitMQ:UseInMemory", "true");

                builder.ConfigureServices(services =>
                {
                    var descriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<RequisicionesDbContext>));
                    if (descriptor != null)
                    {
                        services.Remove(descriptor);
                    }

                    var configuraciones = services.Where(d =>
                            d.ServiceType.IsGenericType &&
                            d.ServiceType.Name.StartsWith("IDbContextOptionsConfiguration") &&
                            d.ServiceType.GenericTypeArguments[0] == typeof(RequisicionesDbContext))
                        .ToList();
                    foreach (var configuracion in configuraciones)
                    {
                        services.Remove(configuracion);
                    }

                    services.AddDbContext<RequisicionesDbContext>(options =>
                    {
                        options.UseInMemoryDatabase(dbName);
                    });

                    customServices?.Invoke(services);

                    using var sp = services.BuildServiceProvider();
                    using var scope = sp.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<RequisicionesDbContext>();
                    db.Database.EnsureCreated();
                });
            });
        }

        private static string GenerateJwtToken(string role, Guid? userId = null)
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

        private static async Task AssertProblemDetails409Async(
            HttpResponseMessage response,
            string expectedCorrelationId,
            string? expectedDetailSubstring = null)
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            Assert.Equal(409, root.GetProperty("status").GetInt32());
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("title").GetString()));
            var detail = root.GetProperty("detail").GetString();
            Assert.False(string.IsNullOrWhiteSpace(detail));
            if (expectedDetailSubstring != null)
            {
                Assert.Contains(expectedDetailSubstring, detail, StringComparison.OrdinalIgnoreCase);
            }
            Assert.Equal(expectedCorrelationId, root.GetProperty("correlationId").GetString());
        }

        [Fact]
        public void ModeloEfCore_RequisicionRowVersion_EstaConfiguradoComoConcurrencyTokenYRowVersion()
        {
            var options = new DbContextOptionsBuilder<RequisicionesDbContext>()
                .UseInMemoryDatabase($"DB_Req_Model_{Guid.NewGuid()}")
                .Options;

            using var context = new RequisicionesDbContext(options);
            var entityType = context.Model.FindEntityType(typeof(Requisicion));
            Assert.NotNull(entityType);

            var rowVersionProperty = entityType.FindProperty(nameof(Requisicion.RowVersion));
            Assert.NotNull(rowVersionProperty);
            Assert.True(rowVersionProperty.IsConcurrencyToken);
            Assert.Equal(ValueGenerated.OnAddOrUpdate, rowVersionProperty.ValueGenerated);
        }

        [Fact]
        public async Task AprobarYRechazarSimultaneos_TaskWhenAll_SoloUnoGana200YElOtroRecibe409ProblemDetailsConCorrelationId()
        {
            using var factory = CreateIsolatedFactory();

            // 1. Capataz crea una requisición en estado Pendiente
            var clientCapataz = factory.CreateClient();
            clientCapataz.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Capataz"));

            var postResp = await clientCapataz.PostAsJsonAsync("/api/requisiciones", new CrearRequisicionRequestDto
            {
                ObraId = Guid.NewGuid(),
                Etapa = "Estructura",
                Observaciones = "Requisición para prueba de concurrencia aprobar vs rechazar",
                Items = new List<CrearRequisicionItemDto>
                {
                    new() { ArticuloId = CementoSeedId, Cantidad = 40m }
                }
            });
            Assert.Equal(HttpStatusCode.Created, postResp.StatusCode);
            var creada = await postResp.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.NotNull(creada);

            // 2. Dos compradores distintos disparan al mismo tiempo aprobar y rechazar
            var correlationId = Guid.NewGuid().ToString();
            var clientComprador1 = factory.CreateClient();
            clientComprador1.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
            clientComprador1.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Comprador", Guid.NewGuid()));

            var clientComprador2 = factory.CreateClient();
            clientComprador2.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
            clientComprador2.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Comprador", Guid.NewGuid()));

            var respuestas = await Task.WhenAll(
                clientComprador1.PostAsync($"/api/requisiciones/{creada.RequisicionId}/aprobar", null),
                clientComprador2.PostAsJsonAsync(
                    $"/api/requisiciones/{creada.RequisicionId}/rechazar",
                    new RechazarRequisicionRequestDto { Motivo = "Rechazo concurrente por otro comprador" }));

            var exitosas = respuestas.Where(r => r.StatusCode == HttpStatusCode.OK).ToList();
            var conflictos = respuestas.Where(r => r.StatusCode == HttpStatusCode.Conflict).ToList();

            Assert.Single(exitosas);
            var conflicto = Assert.Single(conflictos);
            await AssertProblemDetails409Async(conflicto, correlationId);

            // El estado final en la BD debe coincidir exactamente con el comprador que ganó
            var dtoGanador = await exitosas[0].Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.NotNull(dtoGanador);

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RequisicionesDbContext>();
            var enBase = await db.Requisiciones.AsNoTracking().SingleAsync(r => r.Id == creada.RequisicionId);
            Assert.Equal(dtoGanador.Estado, enBase.Estado);
        }

        [Fact]
        public async Task ConflictoDeConcurrenciaAlAprobar_CuandoOtroCompradorRechazoEntreLecturaYGuardado_LanzaDbUpdateConcurrencyException_AbortaEventoYOutbox_YEndpointDevuelve409()
        {
            var dbName = $"DB_Req_RaceCondition_{Guid.NewGuid()}";
            var options = new DbContextOptionsBuilder<RequisicionesDbContext>()
                .UseInMemoryDatabase(dbName)
                .Options;

            var requisicionId = Guid.NewGuid();
            var obraId = Guid.NewGuid();
            var solicitanteId = Guid.NewGuid();

            // 1. Sembramos la requisición en estado Pendiente (RowVersion inicial v1)
            await using (var dbSeed = new RequisicionesDbContext(options))
            {
                var req = new Requisicion(
                    requisicionId,
                    "REQ-2026-0777",
                    obraId,
                    solicitanteId,
                    new[]
                    {
                        new RequisicionItem(Guid.NewGuid(), requisicionId, CementoSeedId, "Cemento Portland", 50m, UnidadesMedida.Bolsa)
                    },
                    etapa: "Estructura");
                dbSeed.Requisiciones.Add(req);
                await dbSeed.SaveChangesAsync();
                Assert.NotEmpty(req.RowVersion);
            }

            // 2. Dos contextos (Comprador A que va a aprobar y Comprador B que va a rechazar)
            // leen simultáneamente la misma requisición en estado Pendiente con el mismo RowVersion v1.
            await using var dbCompradorAprobar = new RequisicionesDbContext(options);
            await using var dbCompradorRechazar = new RequisicionesDbContext(options);

            var repoAprobar = new RequisicionRepository(dbCompradorAprobar);
            var repoRechazar = new RequisicionRepository(dbCompradorRechazar);

            // Precargamos la entidad en el ChangeTracker del Comprador A mientras todavía está Pendiente (v1)
            var reqEnContextoAprobar = await repoAprobar.GetByIdAsync(requisicionId);
            Assert.NotNull(reqEnContextoAprobar);
            Assert.Equal(EstadoRequisicion.Pendiente, reqEnContextoAprobar.Estado);

            // 3. El Comprador B rechaza y guarda primero -> cambia el estado a Rechazada e incrementa RowVersion a v2
            var rechazarHandler = new RechazarRequisicionCommandHandler(repoRechazar);
            var rechazadaDto = await rechazarHandler.Handle(
                new RechazarRequisicionCommand(requisicionId, Guid.NewGuid(), "Presupuesto excedido"),
                CancellationToken.None);
            Assert.Equal(EstadoRequisicion.Rechazada, rechazadaDto.Estado);

            // 4. El Comprador A (que ya tenía leída la versión v1 en estado Pendiente) intenta aprobar:
            // Simulamos también un OutboxMessage agregado al contexto antes de SaveChangesAsync para verificar que se descarta
            dbCompradorAprobar.Set<OutboxMessage>().Add(new OutboxMessage
            {
                MessageId = Guid.NewGuid(),
                ConversationId = Guid.NewGuid(),
                CorrelationId = Guid.NewGuid(),
                SentTime = DateTime.UtcNow,
                Headers = "{}",
                Properties = "{}",
                ContentType = "application/vnd.masstransit+json",
                MessageType = "urn:message:ObraSync.Contracts.Events:RequisicionAprobadaEvent",
                Body = "{}"
            });

            var publishEndpointMock = Substitute.For<IPublishEndpoint>();
            var publisher = new MassTransitRequisicionEventPublisher(publishEndpointMock, dbCompradorAprobar);
            var aprobarHandler = new AprobarRequisicionCommandHandler(repoAprobar, publisher);

            // Al guardar, SaveChangesAsync detecta que RowVersion en la base (v2) != OriginalValue (v1)
            // y lanza DbUpdateConcurrencyException
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
                aprobarHandler.Handle(
                    new AprobarRequisicionCommand(requisicionId, Guid.NewGuid(), Guid.NewGuid()),
                    CancellationToken.None));

            // 5. Verificación crítica: NO se publicó el evento al bus y NO quedó ningún OutboxMessage persistido en la BD,
            // y la requisición sigue en estado Rechazada
            await publishEndpointMock.DidNotReceiveWithAnyArgs()
                .Publish(Arg.Any<RequisicionAprobadaEvent>(), Arg.Any<IPipe<PublishContext<RequisicionAprobadaEvent>>>(), Arg.Any<CancellationToken>());

            await using (var dbVerificacion = new RequisicionesDbContext(options))
            {
                var estadoFinal = await dbVerificacion.Requisiciones.AsNoTracking().SingleAsync(r => r.Id == requisicionId);
                Assert.Equal(EstadoRequisicion.Rechazada, estadoFinal.Estado);
                Assert.Null(estadoFinal.AprobadorId);
                Assert.Equal(0, await dbVerificacion.Set<OutboxMessage>().CountAsync());
            }

            // 6. Además, verificamos vía HTTP en el controlador que cuando SaveChangesAsync lanza DbUpdateConcurrencyException
            // el endpoint responde 409 Conflict con ProblemDetails ("La requisición cambió, recargá e intentá nuevamente.") y correlationId
            var repoMockConcurrencia = Substitute.For<IRequisicionRepository>();
            var reqPendiente = new Requisicion(
                requisicionId,
                "REQ-2026-0777",
                obraId,
                solicitanteId,
                new[]
                {
                    new RequisicionItem(Guid.NewGuid(), requisicionId, CementoSeedId, "Cemento Portland", 50m, UnidadesMedida.Bolsa)
                });
            repoMockConcurrencia.GetByIdAsync(requisicionId, Arg.Any<CancellationToken>())
                .Returns(reqPendiente);
            repoMockConcurrencia.SaveChangesAsync(Arg.Any<CancellationToken>())
                .Returns<Task>(_ => throw new DbUpdateConcurrencyException("Conflicto simulado de RowVersion"));

            using var factoryConColision = CreateIsolatedFactory(services =>
            {
                services.AddScoped<IRequisicionRepository>(_ => repoMockConcurrencia);
            });

            var correlationId = Guid.NewGuid().ToString();
            var client = factoryConColision.CreateClient();
            client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Comprador"));

            var respAprobar409 = await client.PostAsync($"/api/requisiciones/{requisicionId}/aprobar", null);
            await AssertProblemDetails409Async(respAprobar409, correlationId, "La requisición cambió, recargá");

            // Reiniciamos a Pendiente para probar también el 409 de concurrencia en /rechazar
            var reqPendienteRechazar = new Requisicion(
                requisicionId,
                "REQ-2026-0778",
                obraId,
                solicitanteId,
                new[]
                {
                    new RequisicionItem(Guid.NewGuid(), requisicionId, CementoSeedId, "Cemento Portland", 50m, UnidadesMedida.Bolsa)
                });
            repoMockConcurrencia.GetByIdAsync(requisicionId, Arg.Any<CancellationToken>())
                .Returns(reqPendienteRechazar);

            var respRechazar409 = await client.PostAsJsonAsync(
                $"/api/requisiciones/{requisicionId}/rechazar",
                new RechazarRequisicionRequestDto { Motivo = "Intento con colisión de RowVersion" });
            await AssertProblemDetails409Async(respRechazar409, correlationId, "La requisición cambió, recargá");
        }
    }
}
