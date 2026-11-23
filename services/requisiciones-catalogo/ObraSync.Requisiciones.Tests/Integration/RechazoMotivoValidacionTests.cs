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
using ObraSync.Requisiciones.Application.DTOs;
using ObraSync.Requisiciones.Application.Interfaces;
using ObraSync.Requisiciones.Domain.Constants;
using ObraSync.Requisiciones.Infrastructure.Persistence;
using Xunit;

namespace ObraSync.Requisiciones.Tests.Integration
{
    /// <summary>
    /// Pruebas de integración para la validación del motivo de rechazo (#250):
    /// bloquea texto basura (ej. "asdasdas"), categorías fuera de lista y "Otro" sin comentario suficiente
    /// devolviendo 400 ProblemDetails con correlationId; acepta motivos válidos de la lista y comentarios descriptivos.
    /// </summary>
    public class RechazoMotivoValidacionTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private const string TestJwtSecret = "ClaveDePruebaExclusivaParaIntegracionConLongitudMinima32Bytes!";
        private static readonly Guid CementoSeedId = Guid.Parse("11111111-1111-1111-1111-111111110001");

        private readonly WebApplicationFactory<Program> _baseFactory;

        public RechazoMotivoValidacionTests(WebApplicationFactory<Program> factory)
        {
            _baseFactory = factory;
        }

        private WebApplicationFactory<Program> CreateIsolatedFactory()
        {
            var dbName = $"DB_Req_RechazoVal_{Guid.NewGuid()}";

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

                    var validadorObra = Substitute.For<IObraAsignacionValidator>();
                    validadorObra.EstaAsignadaACapatazAsync(
                            Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                        .Returns(true);
                    services.AddSingleton(validadorObra);

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

        private static async Task<Guid> CrearRequisicionPendienteAsync(
            HttpClient client,
            Guid capatazId,
            Guid obraId,
            decimal cantidad = 40m)
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Capataz", capatazId));

            var resp = await client.PostAsJsonAsync("/api/requisiciones", new CrearRequisicionRequestDto
            {
                ObraId = obraId,
                Etapa = "Estructura",
                Items = new List<CrearRequisicionItemDto>
                {
                    new() { ArticuloId = CementoSeedId, Cantidad = cantidad }
                }
            });
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            var dto = await resp.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.NotNull(dto);
            return dto!.RequisicionId;
        }

        private static async Task AssertBadRequestProblemDetailsAsync(HttpResponseMessage response, string correlationId)
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            Assert.Equal(400, root.GetProperty("status").GetInt32());
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("detail").GetString()));
            Assert.Equal(correlationId, root.GetProperty("correlationId").GetString());
        }

        [Fact]
        public async Task GetMotivosRechazo_ConRolAutenticado_RetornaListaOficial()
        {
            using var factory = CreateIsolatedFactory();
            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Comprador"));

            var resp = await client.GetAsync("/api/requisiciones/motivos-rechazo");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var motivos = await resp.Content.ReadFromJsonAsync<List<string>>();
            Assert.NotNull(motivos);
            Assert.Equal(MotivosRechazoRequisicion.Permitidos, motivos);
        }

        [Fact]
        public async Task RechazarRequisicion_ConTextoBasuraCategoriaInvalidaOOtroCorto_Devuelve400ProblemDetailsYNoRechaza()
        {
            using var factory = CreateIsolatedFactory();
            var client = factory.CreateClient();
            var reqId = await CrearRequisicionPendienteAsync(client, Guid.NewGuid(), Guid.NewGuid());

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Comprador"));

            var payloadsInvalidos = new[]
            {
                new RechazarRequisicionRequestDto { Motivo = "asdasdas" },
                new RechazarRequisicionRequestDto { Motivo = "asdfasdf" },
                new RechazarRequisicionRequestDto { Motivo = "1234567890" },
                new RechazarRequisicionRequestDto { CategoriaMotivo = "MotivoQueNoExiste" },
                new RechazarRequisicionRequestDto { CategoriaMotivo = MotivosRechazoRequisicion.Otro },
                new RechazarRequisicionRequestDto { CategoriaMotivo = MotivosRechazoRequisicion.Otro, Comentario = "corto" },
                new RechazarRequisicionRequestDto { CategoriaMotivo = MotivosRechazoRequisicion.Otro, Comentario = "asdasdasdasd" },
                new RechazarRequisicionRequestDto { CategoriaMotivo = MotivosRechazoRequisicion.FaltaPresupuesto, Comentario = "asdasdas" }
            };

            foreach (var payload in payloadsInvalidos)
            {
                var correlationId = Guid.NewGuid().ToString();
                using var reqMsg = new HttpRequestMessage(HttpMethod.Post, $"/api/requisiciones/{reqId}/rechazar")
                {
                    Content = JsonContent.Create(payload)
                };
                reqMsg.Headers.Add("X-Correlation-Id", correlationId);

                var resp = await client.SendAsync(reqMsg);
                await AssertBadRequestProblemDetailsAsync(resp, correlationId);
            }

            // Verificar que la requisición siguió Pendiente tras todos los intentos inválidos
            var estadoActual = await client.GetFromJsonAsync<RequisicionResponseDto>($"/api/requisiciones/{reqId}");
            Assert.NotNull(estadoActual);
            Assert.Equal(EstadoRequisicion.Pendiente, estadoActual!.Estado);
            Assert.Empty(estadoActual.HistorialRechazos);
        }

        [Fact]
        public async Task RechazarRequisicion_ConCategoriaDeListaYConOtroValido_AceptaYRegistraEnHistorial()
        {
            using var factory = CreateIsolatedFactory();
            var client = factory.CreateClient();
            var capatazId = Guid.NewGuid();
            var obraId = Guid.NewGuid();

            var req1Id = await CrearRequisicionPendienteAsync(client, capatazId, obraId, 40m);
            var req2Id = await CrearRequisicionPendienteAsync(client, capatazId, obraId, 50m);
            var req3Id = await CrearRequisicionPendienteAsync(client, capatazId, obraId, 60m);

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Comprador"));

            // 1. Categoría pura sin comentario adicional
            var resp1 = await client.PostAsJsonAsync(
                $"/api/requisiciones/{req1Id}/rechazar",
                new RechazarRequisicionRequestDto { CategoriaMotivo = MotivosRechazoRequisicion.FaltaPresupuesto });
            Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);
            var dto1 = await resp1.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.Equal(MotivosRechazoRequisicion.FaltaPresupuesto, dto1!.MotivoRechazo);
            Assert.Single(dto1.HistorialRechazos);

            // 2. Categoría con comentario aclaratorio opcional
            var resp2 = await client.PostAsJsonAsync(
                $"/api/requisiciones/{req2Id}/rechazar",
                new RechazarRequisicionRequestDto
                {
                    CategoriaMotivo = MotivosRechazoRequisicion.CantidadIncorrecta,
                    Comentario = "Corregir a 20 bolsas según cómputo"
                });
            Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
            var dto2 = await resp2.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.Equal("Cantidad incorrecta: Corregir a 20 bolsas según cómputo", dto2!.MotivoRechazo);

            // 3. Categoría "Otro" con comentario >= 10 caracteres válido
            var resp3 = await client.PostAsJsonAsync(
                $"/api/requisiciones/{req3Id}/rechazar",
                new RechazarRequisicionRequestDto
                {
                    CategoriaMotivo = MotivosRechazoRequisicion.Otro,
                    Comentario = "Se posterga la etapa por cambio de plano estructural"
                });
            Assert.Equal(HttpStatusCode.OK, resp3.StatusCode);
            var dto3 = await resp3.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.Equal("Otro: Se posterga la etapa por cambio de plano estructural", dto3!.MotivoRechazo);
        }
    }
}
