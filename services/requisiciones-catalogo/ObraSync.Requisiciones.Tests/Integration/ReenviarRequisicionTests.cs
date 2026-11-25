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
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using ObraSync.Contracts.Events;
using ObraSync.Requisiciones.Application.DTOs;
using ObraSync.Requisiciones.Application.Interfaces;
using ObraSync.Requisiciones.Domain.Constants;
using ObraSync.Requisiciones.Infrastructure.Persistence;
using Xunit;

namespace ObraSync.Requisiciones.Tests.Integration
{
    public class ReenviarRequisicionTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private const string TestJwtSecret = "ClaveDePruebaExclusivaParaIntegracionConLongitudMinima32Bytes!";
        private static readonly Guid CementoSeedId = Guid.Parse("11111111-1111-1111-1111-111111110001");
        private static readonly Guid CalSeedId = Guid.Parse("11111111-1111-1111-1111-111111110003");

        private readonly WebApplicationFactory<Program> _baseFactory;

        public ReenviarRequisicionTests(WebApplicationFactory<Program> factory)
        {
            _baseFactory = factory;
        }

        private WebApplicationFactory<Program> CreateIsolatedFactory(Action<IServiceCollection>? customServices = null)
        {
            var dbName = $"DB_Req_Reenviar_{Guid.NewGuid()}";

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

        private static async Task AssertProblemDetailsAsync(
            HttpResponseMessage response,
            HttpStatusCode expectedStatus,
            string expectedCorrelationId)
        {
            Assert.Equal(expectedStatus, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            Assert.Equal((int)expectedStatus, root.GetProperty("status").GetInt32());
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("title").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("detail").GetString()));
            Assert.Equal(expectedCorrelationId, root.GetProperty("correlationId").GetString());
        }

        [Fact]
        public async Task FlujoCompleto_Rechazada_EditadaPut_Pendiente_Aprobada_ConservaNumeroEHistorialYPublicaEventoSoloAlAprobar()
        {
            var eventPublisherSpy = Substitute.For<IRequisicionEventPublisher>();

            using var factory = CreateIsolatedFactory(services =>
            {
                services.AddScoped<IRequisicionEventPublisher>(_ => eventPublisherSpy);
            });

            var capatazId = Guid.NewGuid();
            var compradorId = Guid.NewGuid();
            var obraId = Guid.NewGuid();

            var clientCapataz = factory.CreateClient();
            clientCapataz.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Capataz", capatazId));

            var clientComprador = factory.CreateClient();
            clientComprador.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Comprador", compradorId));

            // 1. Capataz crea requisición inicial (80 bolsas de Cemento)
            var crearResp = await clientCapataz.PostAsJsonAsync("/api/requisiciones", new CrearRequisicionRequestDto
            {
                ObraId = obraId,
                Etapa = "Estructura",
                Observaciones = "Pedido inicial de estructura",
                Items = new List<CrearRequisicionItemDto>
                {
                    new() { ArticuloId = CementoSeedId, Cantidad = 80m }
                }
            });
            Assert.Equal(HttpStatusCode.Created, crearResp.StatusCode);
            var creada = await crearResp.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.NotNull(creada);
            Assert.Equal(EstadoRequisicion.Pendiente, creada.Estado);
            Assert.Empty(creada.HistorialRechazos);

            // 2. Comprador rechaza la requisición con motivo
            var rechazarResp = await clientComprador.PostAsJsonAsync(
                $"/api/requisiciones/{creada.RequisicionId}/rechazos",
                new RechazarRequisicionRequestDto { Motivo = "Excede el presupuesto asignado a Estructura" });
            Assert.Equal(HttpStatusCode.OK, rechazarResp.StatusCode);
            var rechazada = await rechazarResp.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.NotNull(rechazada);
            Assert.Equal(EstadoRequisicion.Rechazada, rechazada.Estado);
            Assert.Equal(compradorId, rechazada.RechazadorId);
            Assert.Equal("Excede el presupuesto asignado a Estructura", rechazada.MotivoRechazo);
            Assert.Single(rechazada.HistorialRechazos);

            // 3. El Capataz dueño edita y reenvía la requisición rechazada (PUT /api/requisiciones/{id})
            var putResp = await clientCapataz.PutAsJsonAsync(
                $"/api/requisiciones/{creada.RequisicionId}",
                new EditarRequisicionRequestDto
                {
                    Etapa = "Mampostería",
                    Observaciones = "Cantidad ajustada según observación de Compras",
                    Items = new List<CrearRequisicionItemDto>
                    {
                        new() { ArticuloId = CementoSeedId, Cantidad = 40m },
                        new() { ArticuloId = CalSeedId, Cantidad = 25m }
                    }
                });
            Assert.Equal(HttpStatusCode.OK, putResp.StatusCode);

            var reenviada = await putResp.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.NotNull(reenviada);

            // Conserva número, Id, ObraId y SolicitanteId, vuelve a Pendiente y limpia el rechazo activo de cabecera
            Assert.Equal(creada.RequisicionId, reenviada.RequisicionId);
            Assert.Equal(creada.NumeroRequisicion, reenviada.NumeroRequisicion);
            Assert.Equal(obraId, reenviada.ObraId);
            Assert.Equal(capatazId, reenviada.SolicitanteId);
            Assert.Equal(EstadoRequisicion.Pendiente, reenviada.Estado);
            Assert.Equal("Mampostería", reenviada.Etapa);
            Assert.Equal("Cantidad ajustada según observación de Compras", reenviada.Observaciones);
            Assert.Null(reenviada.MotivoRechazo);
            Assert.Null(reenviada.RechazadorId);
            Assert.Null(reenviada.FechaRechazo);

            // Reemplazó los ítems y conserva el rechazo anterior en HistorialRechazos
            Assert.Equal(2, reenviada.Items.Count);
            Assert.Contains(reenviada.Items, i => i.ArticuloId == CementoSeedId && i.Cantidad == 40m);
            Assert.Contains(reenviada.Items, i => i.ArticuloId == CalSeedId && i.Cantidad == 25m);

            var rechazoHistorico = Assert.Single(reenviada.HistorialRechazos);
            Assert.Equal(compradorId, rechazoHistorico.RechazadorId);
            Assert.Equal("Excede el presupuesto asignado a Estructura", rechazoHistorico.Motivo);

            // Durante el PUT NO se publicó ningún evento RequisicionAprobadaEvent
            await eventPublisherSpy.DidNotReceiveWithAnyArgs()
                .PublishRequisicionAprobadaAsync(default!, default);

            // 4. Compras aprueba la requisición reenviada -> pasa a Aprobada y recién ahí publica RequisicionAprobadaEvent con los ítems corregidos
            var aprobarResp = await clientComprador.PostAsync(
                $"/api/requisiciones/{creada.RequisicionId}/aprobaciones",
                null);
            Assert.Equal(HttpStatusCode.OK, aprobarResp.StatusCode);

            var aprobada = await aprobarResp.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.NotNull(aprobada);
            Assert.Equal(EstadoRequisicion.Aprobada, aprobada.Estado);
            Assert.Single(aprobada.HistorialRechazos);
            Assert.Equal("Excede el presupuesto asignado a Estructura", aprobada.HistorialRechazos[0].Motivo);

            await eventPublisherSpy.Received(1).PublishRequisicionAprobadaAsync(
                Arg.Is<RequisicionAprobadaEvent>(ev =>
                    ev.RequisicionId == creada.RequisicionId &&
                    ev.NumeroRequisicion == creada.NumeroRequisicion &&
                    ev.Etapa == "Mampostería" &&
                    ev.Items.Count == 2),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task PutRequisicion_EnPendienteSeEdita_YEnAprobadaDevuelve409ConflictConProblemDetailsYCorrelationId()
        {
            using var factory = CreateIsolatedFactory();
            var capatazId = Guid.NewGuid();
            var compradorId = Guid.NewGuid();
            var correlationId = Guid.NewGuid().ToString();

            var clientCapataz = factory.CreateClient();
            clientCapataz.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
            clientCapataz.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Capataz", capatazId));

            var clientComprador = factory.CreateClient();
            clientComprador.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Comprador", compradorId));

            var crearResp = await clientCapataz.PostAsJsonAsync("/api/requisiciones", new CrearRequisicionRequestDto
            {
                ObraId = Guid.NewGuid(),
                Etapa = "Estructura",
                Items = new List<CrearRequisicionItemDto>
                {
                    new() { ArticuloId = CementoSeedId, Cantidad = 30m }
                }
            });
            var creada = await crearResp.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.NotNull(creada);

            var editPayload = new EditarRequisicionRequestDto
            {
                Etapa = "Terminaciones",
                Observaciones = "Intento de edición inválida",
                Items = new List<CrearRequisicionItemDto>
                {
                    new() { ArticuloId = CalSeedId, Cantidad = 15m }
                }
            };

            // 1. Editar mientras está Pendiente -> 200 y sigue Pendiente (antes de que Compras la apruebe)
            var putPendienteResp = await clientCapataz.PutAsJsonAsync(
                $"/api/requisiciones/{creada.RequisicionId}",
                editPayload);
            Assert.Equal(HttpStatusCode.OK, putPendienteResp.StatusCode);
            var editada = await putPendienteResp.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.NotNull(editada);
            Assert.Equal(EstadoRequisicion.Pendiente, editada.Estado);
            Assert.Equal("Terminaciones", editada.Etapa);
            Assert.Equal(creada.NumeroRequisicion, editada.NumeroRequisicion);

            // 2. Comprador aprueba la requisición -> Intentar editar estando Aprobada -> 409 Conflict
            var aprobarResp = await clientComprador.PostAsync(
                $"/api/requisiciones/{creada.RequisicionId}/aprobaciones",
                null);
            Assert.Equal(HttpStatusCode.OK, aprobarResp.StatusCode);

            var putAprobadaResp = await clientCapataz.PutAsJsonAsync(
                $"/api/requisiciones/{creada.RequisicionId}",
                editPayload);
            await AssertProblemDetailsAsync(putAprobadaResp, HttpStatusCode.Conflict, correlationId);
        }

        [Fact]
        public async Task CancelarRequisicion_PendienteDelDueno_PasaACancelada_YYaNoSePuedeAprobarNiEditarNiCancelarDeNuevo()
        {
            using var factory = CreateIsolatedFactory();
            var capatazId = Guid.NewGuid();
            var correlationId = Guid.NewGuid().ToString();

            var clientCapataz = factory.CreateClient();
            clientCapataz.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
            clientCapataz.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Capataz", capatazId));

            var clientComprador = factory.CreateClient();
            clientComprador.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Comprador", Guid.NewGuid()));

            var crearResp = await clientCapataz.PostAsJsonAsync("/api/requisiciones", new CrearRequisicionRequestDto
            {
                ObraId = Guid.NewGuid(),
                Etapa = "Estructura",
                Items = new List<CrearRequisicionItemDto> { new() { ArticuloId = CementoSeedId, Cantidad = 30m } }
            });
            var creada = await crearResp.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.NotNull(creada);

            var cancelarResp = await clientCapataz.PostAsync($"/api/requisiciones/{creada.RequisicionId}/cancelaciones", null);
            Assert.Equal(HttpStatusCode.OK, cancelarResp.StatusCode);
            var cancelada = await cancelarResp.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.NotNull(cancelada);
            Assert.Equal(EstadoRequisicion.Cancelada, cancelada.Estado);

            // Una cancelada no se puede aprobar, ni editar, ni volver a cancelar
            var aprobarResp = await clientComprador.PostAsync($"/api/requisiciones/{creada.RequisicionId}/aprobaciones", null);
            Assert.Equal(HttpStatusCode.Conflict, aprobarResp.StatusCode);

            var putResp = await clientCapataz.PutAsJsonAsync($"/api/requisiciones/{creada.RequisicionId}", new EditarRequisicionRequestDto
            {
                Items = new List<CrearRequisicionItemDto> { new() { ArticuloId = CalSeedId, Cantidad = 5m } }
            });
            await AssertProblemDetailsAsync(putResp, HttpStatusCode.Conflict, correlationId);

            var otraVez = await clientCapataz.PostAsync($"/api/requisiciones/{creada.RequisicionId}/cancelaciones", null);
            await AssertProblemDetailsAsync(otraVez, HttpStatusCode.Conflict, correlationId);
        }

        [Fact]
        public async Task CancelarRequisicion_AprobadaDevuelve409_YOtroCapatazDevuelve403_YCompradorNoPuede()
        {
            using var factory = CreateIsolatedFactory();
            var capatazId = Guid.NewGuid();

            var clientCapataz = factory.CreateClient();
            clientCapataz.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Capataz", capatazId));
            var clientOtro = factory.CreateClient();
            clientOtro.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Capataz", Guid.NewGuid()));
            var clientComprador = factory.CreateClient();
            clientComprador.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Comprador", Guid.NewGuid()));

            var crearResp = await clientCapataz.PostAsJsonAsync("/api/requisiciones", new CrearRequisicionRequestDto
            {
                ObraId = Guid.NewGuid(),
                Items = new List<CrearRequisicionItemDto> { new() { ArticuloId = CementoSeedId, Cantidad = 10m } }
            });
            var creada = await crearResp.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.NotNull(creada);

            Assert.Equal(HttpStatusCode.Forbidden,
                (await clientOtro.PostAsync($"/api/requisiciones/{creada.RequisicionId}/cancelaciones", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await clientComprador.PostAsync($"/api/requisiciones/{creada.RequisicionId}/cancelaciones", null)).StatusCode);

            Assert.Equal(HttpStatusCode.OK,
                (await clientComprador.PostAsync($"/api/requisiciones/{creada.RequisicionId}/aprobaciones", null)).StatusCode);

            Assert.Equal(HttpStatusCode.Conflict,
                (await clientCapataz.PostAsync($"/api/requisiciones/{creada.RequisicionId}/cancelaciones", null)).StatusCode);
        }

        [Fact]
        public async Task PutRequisicion_OtroCapatazNoDuenoRecibe403Forbidden_YAdminSiPuedeReenviar()
        {
            using var factory = CreateIsolatedFactory();
            var capatazDuenoId = Guid.NewGuid();
            var otroCapatazId = Guid.NewGuid();
            var compradorId = Guid.NewGuid();
            var adminId = Guid.NewGuid();
            var correlationId = Guid.NewGuid().ToString();

            var clientCapatazDueno = factory.CreateClient();
            clientCapatazDueno.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Capataz", capatazDuenoId));

            var clientComprador = factory.CreateClient();
            clientComprador.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Comprador", compradorId));

            // 1. Capataz dueño crea la requisición y Comprador la rechaza
            var crearResp = await clientCapatazDueno.PostAsJsonAsync("/api/requisiciones", new CrearRequisicionRequestDto
            {
                ObraId = Guid.NewGuid(),
                Etapa = "Fundaciones",
                Items = new List<CrearRequisicionItemDto>
                {
                    new() { ArticuloId = CementoSeedId, Cantidad = 60m }
                }
            });
            var creada = await crearResp.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.NotNull(creada);

            await clientComprador.PostAsJsonAsync(
                $"/api/requisiciones/{creada.RequisicionId}/rechazos",
                new RechazarRequisicionRequestDto { Motivo = "Revisar cantidades de fundaciones" });

            var editPayload = new EditarRequisicionRequestDto
            {
                Etapa = "Fundaciones",
                Observaciones = "Corregido",
                Items = new List<CrearRequisicionItemDto>
                {
                    new() { ArticuloId = CementoSeedId, Cantidad = 45m }
                }
            };

            // 2. Otro Capataz (que no es el SolicitanteId) intenta editarla -> 403 Forbidden con ProblemDetails y correlationId
            var clientOtroCapataz = factory.CreateClient();
            clientOtroCapataz.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
            clientOtroCapataz.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Capataz", otroCapatazId));

            var putOtroCapatazResp = await clientOtroCapataz.PutAsJsonAsync(
                $"/api/requisiciones/{creada.RequisicionId}",
                editPayload);
            await AssertProblemDetailsAsync(putOtroCapatazResp, HttpStatusCode.Forbidden, correlationId);

            // 3. Un Admin sí puede editarla y reenviarla -> 200 OK
            var clientAdmin = factory.CreateClient();
            clientAdmin.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Admin", adminId));

            var putAdminResp = await clientAdmin.PutAsJsonAsync(
                $"/api/requisiciones/{creada.RequisicionId}",
                editPayload);
            Assert.Equal(HttpStatusCode.OK, putAdminResp.StatusCode);

            var reenviada = await putAdminResp.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.NotNull(reenviada);
            Assert.Equal(EstadoRequisicion.Pendiente, reenviada.Estado);
            Assert.Equal(capatazDuenoId, reenviada.SolicitanteId);
            Assert.Single(reenviada.HistorialRechazos);
        }

        [Fact]
        public async Task PutRequisicion_MultiplesCiclosDeRechazoYReenvio_AcumulaTodoElHistorialEnOrdenCronologico()
        {
            using var factory = CreateIsolatedFactory();
            var capatazId = Guid.NewGuid();
            var comprador1Id = Guid.NewGuid();
            var comprador2Id = Guid.NewGuid();

            var clientCapataz = factory.CreateClient();
            clientCapataz.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Capataz", capatazId));

            var clientComprador1 = factory.CreateClient();
            clientComprador1.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Comprador", comprador1Id));

            var clientComprador2 = factory.CreateClient();
            clientComprador2.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Comprador", comprador2Id));

            // Crear requisición
            var crearResp = await clientCapataz.PostAsJsonAsync("/api/requisiciones", new CrearRequisicionRequestDto
            {
                ObraId = Guid.NewGuid(),
                Etapa = "Estructura",
                Items = new List<CrearRequisicionItemDto>
                {
                    new() { ArticuloId = CementoSeedId, Cantidad = 100m }
                }
            });
            var creada = await crearResp.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.NotNull(creada);

            // Ciclo 1: Rechazo 1 -> Reenvío 1
            await clientComprador1.PostAsJsonAsync(
                $"/api/requisiciones/{creada.RequisicionId}/rechazos",
                new RechazarRequisicionRequestDto { Motivo = "Motivo 1: bajar bolsas de cemento" });

            var put1Resp = await clientCapataz.PutAsJsonAsync(
                $"/api/requisiciones/{creada.RequisicionId}",
                new EditarRequisicionRequestDto
                {
                    Etapa = "Estructura",
                    Observaciones = "Reenvío 1",
                    Items = new List<CrearRequisicionItemDto>
                    {
                        new() { ArticuloId = CementoSeedId, Cantidad = 60m }
                    }
                });
            Assert.Equal(HttpStatusCode.OK, put1Resp.StatusCode);

            // Ciclo 2: Rechazo 2 -> Reenvío 2
            await clientComprador2.PostAsJsonAsync(
                $"/api/requisiciones/{creada.RequisicionId}/rechazos",
                new RechazarRequisicionRequestDto { Motivo = "Motivo 2: cambiar etapa a Terminaciones" });

            var put2Resp = await clientCapataz.PutAsJsonAsync(
                $"/api/requisiciones/{creada.RequisicionId}",
                new EditarRequisicionRequestDto
                {
                    Etapa = "Terminaciones",
                    Observaciones = "Reenvío 2 definitivo",
                    Items = new List<CrearRequisicionItemDto>
                    {
                        new() { ArticuloId = CementoSeedId, Cantidad = 50m }
                    }
                });
            Assert.Equal(HttpStatusCode.OK, put2Resp.StatusCode);

            var dtoFinal = await put2Resp.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.NotNull(dtoFinal);
            Assert.Equal(EstadoRequisicion.Pendiente, dtoFinal.Estado);
            Assert.Equal(2, dtoFinal.HistorialRechazos.Count);

            Assert.Equal(comprador1Id, dtoFinal.HistorialRechazos[0].RechazadorId);
            Assert.Equal("Motivo 1: bajar bolsas de cemento", dtoFinal.HistorialRechazos[0].Motivo);

            Assert.Equal(comprador2Id, dtoFinal.HistorialRechazos[1].RechazadorId);
            Assert.Equal("Motivo 2: cambiar etapa a Terminaciones", dtoFinal.HistorialRechazos[1].Motivo);
        }
    }
}
