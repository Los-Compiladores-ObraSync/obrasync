using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using ObraSync.Requisiciones.Application.DTOs;
using ObraSync.Requisiciones.Domain.Constants;
using ObraSync.Requisiciones.Infrastructure.Persistence;
using Xunit;

namespace ObraSync.Requisiciones.Tests.Integration
{
    public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private const string TestJwtSecret = "ClaveDePruebaExclusivaParaIntegracionConLongitudMinima32Bytes!";
        private readonly WebApplicationFactory<Program> _factory;

        public HealthEndpointTests(WebApplicationFactory<Program> factory)
        {
            var dbName = $"DB_Req_Test_{Guid.NewGuid()}";

            _factory = factory.WithWebHostBuilder(builder =>
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

                    // EF Core 9 guarda aparte la configuracion de AddDbContext (UseSqlServer). Si no se
                    // quita tambien, quedan registrados SqlServer e InMemory a la vez y el host no arranca.
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
                new Claim("email", "test@obrasync.com"),
                new Claim("name", "Usuario Test"),
                new Claim("role", role)
            };

            var token = new JwtSecurityToken(
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(30),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        [Fact]
        public async Task GetHealth_DebeResponderOkConNombreDeServicioYBaseDeDatos()
        {
            var client = _factory.CreateClient();

            var response = await client.GetAsync("/health");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal("ok", root.GetProperty("status").GetString());
            Assert.Equal("requisiciones-catalogo", root.GetProperty("service").GetString());
            Assert.Equal("DB_Req", root.GetProperty("database").GetString());
        }

        [Fact]
        public async Task GetInsumos_SinToken_DebeRetornarUnauthorized()
        {
            var client = _factory.CreateClient();

            var response = await client.GetAsync("/api/catalogo/insumos");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Theory]
        [InlineData("cemento")]
        [InlineData("CEMENTO")]
        [InlineData("Cemento")]
        [InlineData("cEmEnTo")]
        public async Task GetInsumos_ConParametroSearch_InsensibleAMayusculasYMinusculas_DebeDevolverMismosResultados(string search)
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Capataz"));

            var response = await client.GetAsync($"/api/catalogo/insumos?search={search}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var insumos = await response.Content.ReadFromJsonAsync<List<InsumoCatalogoDto>>();
            Assert.NotNull(insumos);
            Assert.Equal(2, insumos.Count);
            Assert.Equal("MAT-CEM-001", insumos[0].Codigo);
            Assert.Equal("MAT-CEM-002", insumos[1].Codigo);
        }

        [Fact]
        public async Task GetInsumos_ConParametroSearch_SinResultados_DebeDevolverListaVacia()
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Capataz"));

            var response = await client.GetAsync("/api/catalogo/insumos?search=TERMINO_INEXISTENTE_999");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var insumos = await response.Content.ReadFromJsonAsync<List<InsumoCatalogoDto>>();
            Assert.NotNull(insumos);
            Assert.Empty(insumos);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task GetInsumos_ConParametroSearchVacioOEspacios_DebeDevolverTodosLosActivos(string search)
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Comprador"));

            var url = string.IsNullOrWhiteSpace(search) && search.Length > 0
                ? $"/api/catalogo/insumos?search={Uri.EscapeDataString(search)}"
                : "/api/catalogo/insumos?search=";

            var response = await client.GetAsync(url);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var insumos = await response.Content.ReadFromJsonAsync<List<InsumoCatalogoDto>>();
            Assert.NotNull(insumos);
            Assert.True(insumos.Count >= 11);
            Assert.All(insumos, i => Assert.True(i.Activo));
        }

        [Fact]
        public async Task GetInsumos_ConSoloActivosFalso_DebeIncluirInsumosInactivos()
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Comprador"));

            var codigoInactivo = $"MAT-INA-{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}";
            var requestInactivo = new CrearInsumoRequestDto
            {
                Codigo = codigoInactivo,
                Descripcion = "Insumo Especial Fuera de Produccion",
                UnidadMedida = UnidadesMedida.Unidad,
                Moneda = Monedas.PesoArgentino,
                PrecioReferencia = 500m,
                Activo = false
            };

            var postResponse = await client.PostAsJsonAsync("/api/catalogo/insumos", requestInactivo);
            Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);

            // Por defecto soloActivos = true -> no debe incluirlo
            var activosResp = await client.GetAsync("/api/catalogo/insumos?soloActivos=true");
            Assert.Equal(HttpStatusCode.OK, activosResp.StatusCode);
            var activos = await activosResp.Content.ReadFromJsonAsync<List<InsumoCatalogoDto>>();
            Assert.NotNull(activos);
            Assert.DoesNotContain(activos, i => i.Codigo == codigoInactivo);

            // Con soloActivos = false -> debe incluirlo
            var todosResp = await client.GetAsync("/api/catalogo/insumos?soloActivos=false");
            Assert.Equal(HttpStatusCode.OK, todosResp.StatusCode);
            var todos = await todosResp.Content.ReadFromJsonAsync<List<InsumoCatalogoDto>>();
            Assert.NotNull(todos);
            Assert.Contains(todos, i => i.Codigo == codigoInactivo && !i.Activo);
        }

        [Fact]
        public async Task GetInsumos_SensibilidadATildes_DebeDistinguirConYSinTildeSegunCollation()
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Capataz"));

            // Con tilde: "Río" encuentra "Arena Gruesa Lavada de Río"
            var conTildeResp = await client.GetAsync($"/api/catalogo/insumos?search={Uri.EscapeDataString("Río")}");
            Assert.Equal(HttpStatusCode.OK, conTildeResp.StatusCode);
            var conTilde = await conTildeResp.Content.ReadFromJsonAsync<List<InsumoCatalogoDto>>();
            Assert.NotNull(conTilde);
            Assert.Single(conTilde);
            Assert.Equal("MAT-ARI-001", conTilde[0].Codigo);

            // Sin tilde: "rio" no coincide por collation sensible a tildes (_AS)
            var sinTildeResp = await client.GetAsync("/api/catalogo/insumos?search=rio");
            Assert.Equal(HttpStatusCode.OK, sinTildeResp.StatusCode);
            var sinTilde = await sinTildeResp.Content.ReadFromJsonAsync<List<InsumoCatalogoDto>>();
            Assert.NotNull(sinTilde);
            Assert.Empty(sinTilde);

            // Con tilde: "Granítica" encuentra "Piedra Partida Granítica 6-20 mm"
            var conTildeGraniticaResp = await client.GetAsync($"/api/catalogo/insumos?search={Uri.EscapeDataString("Granítica")}");
            Assert.Equal(HttpStatusCode.OK, conTildeGraniticaResp.StatusCode);
            var conTildeGranitica = await conTildeGraniticaResp.Content.ReadFromJsonAsync<List<InsumoCatalogoDto>>();
            Assert.NotNull(conTildeGranitica);
            Assert.Single(conTildeGranitica);
            Assert.Equal("MAT-ARI-002", conTildeGranitica[0].Codigo);

            // Sin tilde: "granitica" no coincide
            var sinTildeGraniticaResp = await client.GetAsync("/api/catalogo/insumos?search=granitica");
            Assert.Equal(HttpStatusCode.OK, sinTildeGraniticaResp.StatusCode);
            var sinTildeGranitica = await sinTildeGraniticaResp.Content.ReadFromJsonAsync<List<InsumoCatalogoDto>>();
            Assert.NotNull(sinTildeGranitica);
            Assert.Empty(sinTildeGranitica);
        }

        [Fact]
        public async Task PostInsumo_ConRolCompradorYDatosValidos_DebeRetornarCreatedConMonedaARS()
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Comprador"));

            var request = new CrearInsumoRequestDto
            {
                Codigo = "MAT-LAD-001",
                Descripcion = "Ladrillo Hueco 12x18x33",
                UnidadMedida = UnidadesMedida.Unidad,
                Moneda = Monedas.PesoArgentino,
                PrecioReferencia = 650.00m,
                Activo = true
            };

            var response = await client.PostAsJsonAsync("/api/catalogo/insumos", request);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var creado = await response.Content.ReadFromJsonAsync<InsumoCatalogoDto>();
            Assert.NotNull(creado);
            Assert.NotEqual(Guid.Empty, creado.ArticuloId);
            Assert.Equal("MAT-LAD-001", creado.Codigo);
            Assert.Equal(Monedas.PesoArgentino, creado.Moneda);
            Assert.Equal(650.00m, creado.PrecioReferencia);
        }

        [Fact]
        public async Task PostInsumo_ConRolCapataz_DebeRetornarForbidden()
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Capataz"));

            var request = new CrearInsumoRequestDto
            {
                Codigo = "MAT-LAD-002",
                Descripcion = "Ladrillo Común",
                UnidadMedida = UnidadesMedida.Unidad
            };

            var response = await client.PostAsJsonAsync("/api/catalogo/insumos", request);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task FlujoPuntaAPunta_CU02_CapatazCreaRequisicionYCompradorAprueba_DebeRetornarAprobada()
        {
            var capatazId = Guid.NewGuid();
            var compradorId = Guid.NewGuid();
            var obraId = Guid.NewGuid();
            var cementoSeedId = Guid.Parse("11111111-1111-1111-1111-111111110001");

            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Correlation-ID", Guid.NewGuid().ToString());

            // 1. Capataz crea requisición de 80 bolsas de cemento para etapa Estructura
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Capataz", capatazId));

            var crearRequest = new CrearRequisicionRequestDto
            {
                ObraId = obraId,
                Etapa = "Estructura",
                Observaciones = "Hormigonado losa piso 2",
                Items = new List<CrearRequisicionItemDto>
                {
                    new() { ArticuloId = cementoSeedId, Cantidad = 80m }
                }
            };

            var createResponse = await client.PostAsJsonAsync("/api/requisiciones", crearRequest);
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

            var requisicionCreada = await createResponse.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.NotNull(requisicionCreada);
            Assert.NotEqual(Guid.Empty, requisicionCreada.RequisicionId);
            Assert.Equal(obraId, requisicionCreada.ObraId);
            Assert.Equal("Estructura", requisicionCreada.Etapa);
            Assert.Equal(capatazId, requisicionCreada.SolicitanteId);
            Assert.Equal(EstadoRequisicion.Pendiente, requisicionCreada.Estado);
            Assert.Null(requisicionCreada.AprobadorId);
            Assert.Single(requisicionCreada.Items);
            Assert.Equal(80m, requisicionCreada.Items[0].Cantidad);
            Assert.Equal(UnidadesMedida.Bolsa, requisicionCreada.Items[0].UnidadMedida);

            // 2. Comprador lista las requisiciones pendientes de esa obra
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Comprador", compradorId));

            var listResponse = await client.GetAsync($"/api/requisiciones?obraId={obraId}&estado=Pendiente");
            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

            var lista = await listResponse.Content.ReadFromJsonAsync<List<RequisicionResponseDto>>();
            Assert.NotNull(lista);
            Assert.Single(lista);

            // 3. Comprador aprueba la requisición (cambia a Aprobada y publica RequisicionAprobadaEvent)
            var aprobarResponse = await client.PostAsync(
                $"/api/requisiciones/{requisicionCreada.RequisicionId}/aprobar",
                content: null);

            Assert.Equal(HttpStatusCode.OK, aprobarResponse.StatusCode);

            using var aprobarJson = JsonDocument.Parse(await aprobarResponse.Content.ReadAsStringAsync());
            Assert.EndsWith("Z", aprobarJson.RootElement.GetProperty("fechaCreacion").GetString());
            Assert.EndsWith("Z", aprobarJson.RootElement.GetProperty("fechaAprobacion").GetString());

            var requisicionAprobada = aprobarJson.RootElement.Deserialize<RequisicionResponseDto>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.NotNull(requisicionAprobada);
            Assert.Equal(EstadoRequisicion.Aprobada, requisicionAprobada.Estado);
            Assert.Equal(compradorId, requisicionAprobada.AprobadorId);
            Assert.NotNull(requisicionAprobada.FechaAprobacion);

            // 4. Si se intenta aprobar nuevamente, retorna 409 Conflict con ProblemDetails
            var segundoIntento = await client.PostAsync(
                $"/api/requisiciones/{requisicionCreada.RequisicionId}/aprobar",
                content: null);

            Assert.Equal(HttpStatusCode.Conflict, segundoIntento.StatusCode);
        }

        [Fact]
        public async Task GetRequisiciones_SinFiltroYConFiltros_DebeDevolverTodosLosEstadosOrdenadosPorFechaDescendente()
        {
            var capatazId = Guid.NewGuid();
            var compradorId = Guid.NewGuid();
            var obra1Id = Guid.NewGuid();
            var obra2Id = Guid.NewGuid();
            var cementoSeedId = Guid.Parse("11111111-1111-1111-1111-111111110001");
            var calSeedId = Guid.Parse("11111111-1111-1111-1111-111111110003");

            var client = _factory.CreateClient();

            // 1. Capataz crea Requisición 1 en Obra 1 y el Comprador la aprueba
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Capataz", capatazId));

            var resp1 = await client.PostAsJsonAsync("/api/requisiciones", new CrearRequisicionRequestDto
            {
                ObraId = obra1Id,
                Etapa = "Fundaciones",
                Items = new List<CrearRequisicionItemDto>
                {
                    new() { ArticuloId = cementoSeedId, Cantidad = 40m }
                }
            });
            Assert.Equal(HttpStatusCode.Created, resp1.StatusCode);
            var req1 = await resp1.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.NotNull(req1);

            await Task.Delay(25);

            // 2. Capataz crea Requisición 2 en Obra 2 (queda Pendiente, más reciente)
            var resp2 = await client.PostAsJsonAsync("/api/requisiciones", new CrearRequisicionRequestDto
            {
                ObraId = obra2Id,
                Etapa = "Mampostería",
                Items = new List<CrearRequisicionItemDto>
                {
                    new() { ArticuloId = calSeedId, Cantidad = 25m }
                }
            });
            Assert.Equal(HttpStatusCode.Created, resp2.StatusCode);
            var req2 = await resp2.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.NotNull(req2);

            // Comprador aprueba Requisición 1
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Comprador", compradorId));

            var aprobarResp = await client.PostAsync($"/api/requisiciones/{req1.RequisicionId}/aprobar", null);
            Assert.Equal(HttpStatusCode.OK, aprobarResp.StatusCode);

            // Escenario 1: GET /api/requisiciones sin filtros devuelve todos los estados ordenados por FechaCreacion descendente y con Items
            var sinFiltroResp = await client.GetAsync("/api/requisiciones");
            Assert.Equal(HttpStatusCode.OK, sinFiltroResp.StatusCode);
            var todas = await sinFiltroResp.Content.ReadFromJsonAsync<List<RequisicionResponseDto>>();
            Assert.NotNull(todas);

            var deEstasObras = todas
                .Where(r => r.ObraId == obra1Id || r.ObraId == obra2Id)
                .ToList();

            Assert.Equal(2, deEstasObras.Count);
            Assert.Equal(req2.RequisicionId, deEstasObras[0].RequisicionId);
            Assert.Equal(EstadoRequisicion.Pendiente, deEstasObras[0].Estado);
            Assert.Single(deEstasObras[0].Items);

            Assert.Equal(req1.RequisicionId, deEstasObras[1].RequisicionId);
            Assert.Equal(EstadoRequisicion.Aprobada, deEstasObras[1].Estado);
            Assert.Single(deEstasObras[1].Items);

            // Escenario 2: Filtro por obraId
            var porObraResp = await client.GetAsync($"/api/requisiciones?obraId={obra1Id}");
            var porObra = await porObraResp.Content.ReadFromJsonAsync<List<RequisicionResponseDto>>();
            Assert.NotNull(porObra);
            Assert.Single(porObra);
            Assert.Equal(req1.RequisicionId, porObra[0].RequisicionId);

            // Escenario 3: Filtro por estado case-insensitive ("APROBADA" y "pendiente")
            var aprobadasMayusResp = await client.GetAsync($"/api/requisiciones?obraId={obra1Id}&estado=APROBADA");
            var aprobadasMayus = await aprobadasMayusResp.Content.ReadFromJsonAsync<List<RequisicionResponseDto>>();
            Assert.NotNull(aprobadasMayus);
            Assert.Single(aprobadasMayus);
            Assert.Equal(EstadoRequisicion.Aprobada, aprobadasMayus[0].Estado);

            var pendientesMinusResp = await client.GetAsync($"/api/requisiciones?obraId={obra2Id}&estado=pendiente");
            var pendientesMinus = await pendientesMinusResp.Content.ReadFromJsonAsync<List<RequisicionResponseDto>>();
            Assert.NotNull(pendientesMinus);
            Assert.Single(pendientesMinus);
            Assert.Equal(EstadoRequisicion.Pendiente, pendientesMinus[0].Estado);

            // Escenario 4: Estado inexistente devuelve lista vacía
            var inexistenteResp = await client.GetAsync("/api/requisiciones?estado=INEXISTENTE");
            Assert.Equal(HttpStatusCode.OK, inexistenteResp.StatusCode);
            var inexistente = await inexistenteResp.Content.ReadFromJsonAsync<List<RequisicionResponseDto>>();
            Assert.NotNull(inexistente);
            Assert.Empty(inexistente);
        }

        [Fact]
        public async Task PostRequisicion_ConMismoIdempotencyKeyYDatosIguales_DebeDevolverMismaRespuestaYCrearUnaSola()
        {
            var capatazId = Guid.NewGuid();
            var obraId = Guid.NewGuid();
            var cementoSeedId = Guid.Parse("11111111-1111-1111-1111-111111110001");
            var idempotencyKey = $"idem-{Guid.NewGuid():N}";

            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Capataz", capatazId));

            var payload = new CrearRequisicionRequestDto
            {
                ObraId = obraId,
                Etapa = "Estructura",
                Observaciones = "Pedido con Idempotency-Key",
                Items = new List<CrearRequisicionItemDto>
                {
                    new() { ArticuloId = cementoSeedId, Cantidad = 50m }
                }
            };

            using var msg1 = new HttpRequestMessage(HttpMethod.Post, "/api/requisiciones")
            {
                Content = JsonContent.Create(payload)
            };
            msg1.Headers.Add("Idempotency-Key", idempotencyKey);

            var resp1 = await client.SendAsync(msg1);
            Assert.Equal(HttpStatusCode.Created, resp1.StatusCode);
            var creada1 = await resp1.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.NotNull(creada1);

            // Segundo envío seguido con el mismo Idempotency-Key y mismos datos
            using var msg2 = new HttpRequestMessage(HttpMethod.Post, "/api/requisiciones")
            {
                Content = JsonContent.Create(payload)
            };
            msg2.Headers.Add("Idempotency-Key", idempotencyKey);

            var resp2 = await client.SendAsync(msg2);
            Assert.Equal(HttpStatusCode.Created, resp2.StatusCode);
            var creada2 = await resp2.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.NotNull(creada2);

            Assert.Equal(creada1.RequisicionId, creada2.RequisicionId);
            Assert.Equal(creada1.NumeroRequisicion, creada2.NumeroRequisicion);

            // Verificar que en la base de datos se haya creado una sola requisición para esa obra
            var listResp = await client.GetAsync($"/api/requisiciones?obraId={obraId}");
            Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
            var lista = await listResp.Content.ReadFromJsonAsync<List<RequisicionResponseDto>>();
            Assert.NotNull(lista);
            Assert.Single(lista);
        }

        [Fact]
        public async Task PostRequisicion_ConMismoIdempotencyKeyPeroDatosDistintos_DebeRetornar409ConflictConProblemDetails()
        {
            var capatazId = Guid.NewGuid();
            var obraId = Guid.NewGuid();
            var cementoSeedId = Guid.Parse("11111111-1111-1111-1111-111111110001");
            var idempotencyKey = $"idem-{Guid.NewGuid():N}";

            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Capataz", capatazId));

            using var msg1 = new HttpRequestMessage(HttpMethod.Post, "/api/requisiciones")
            {
                Content = JsonContent.Create(new CrearRequisicionRequestDto
                {
                    ObraId = obraId,
                    Etapa = "Estructura",
                    Items = new List<CrearRequisicionItemDto>
                    {
                        new() { ArticuloId = cementoSeedId, Cantidad = 50m }
                    }
                })
            };
            msg1.Headers.Add("Idempotency-Key", idempotencyKey);

            var resp1 = await client.SendAsync(msg1);
            Assert.Equal(HttpStatusCode.Created, resp1.StatusCode);
            var creada = await resp1.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.NotNull(creada);

            // Reutiliza el mismo Idempotency-Key pero cambia la cantidad
            using var msg2 = new HttpRequestMessage(HttpMethod.Post, "/api/requisiciones")
            {
                Content = JsonContent.Create(new CrearRequisicionRequestDto
                {
                    ObraId = obraId,
                    Etapa = "Estructura",
                    Items = new List<CrearRequisicionItemDto>
                    {
                        new() { ArticuloId = cementoSeedId, Cantidad = 90m }
                    }
                })
            };
            msg2.Headers.Add("Idempotency-Key", idempotencyKey);

            var resp2 = await client.SendAsync(msg2);
            Assert.Equal(HttpStatusCode.Conflict, resp2.StatusCode);

            var jsonConflict = await resp2.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(jsonConflict);
            Assert.Contains(creada.NumeroRequisicion, doc.RootElement.GetProperty("detail").GetString());
            Assert.Equal(creada.NumeroRequisicion, doc.RootElement.GetProperty("numeroRequisicion").GetString());
        }

        [Fact]
        public async Task PostRequisicion_DosEnviosSeguidosIgualesEnVentanaDe2Minutos_Responde409YCreaUnaSola_EnviosDistintosPasan()
        {
            var capatazId = Guid.NewGuid();
            var obraId = Guid.NewGuid();
            var cementoSeedId = Guid.Parse("11111111-1111-1111-1111-111111110001");
            var calSeedId = Guid.Parse("11111111-1111-1111-1111-111111110003");

            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Capataz", capatazId));

            var payloadOriginal = new CrearRequisicionRequestDto
            {
                ObraId = obraId,
                Etapa = "Mampostería",
                Observaciones = "Primer envío",
                Items = new List<CrearRequisicionItemDto>
                {
                    new() { ArticuloId = cementoSeedId, Cantidad = 30m }
                }
            };

            // 1. Primer envío crea la requisición
            var resp1 = await client.PostAsJsonAsync("/api/requisiciones", payloadOriginal);
            Assert.Equal(HttpStatusCode.Created, resp1.StatusCode);
            var creada1 = await resp1.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.NotNull(creada1);

            // 2. Segundo envío seguido con mismo solicitante, obra, etapa e ítems/cantidades -> 409 Conflict con número de requisición creada
            var respDuplicada = await client.PostAsJsonAsync("/api/requisiciones", payloadOriginal);
            Assert.Equal(HttpStatusCode.Conflict, respDuplicada.StatusCode);

            var problemJson = await respDuplicada.Content.ReadAsStringAsync();
            using var problemDoc = JsonDocument.Parse(problemJson);
            Assert.Equal(409, problemDoc.RootElement.GetProperty("status").GetInt32());
            Assert.Contains(creada1.NumeroRequisicion, problemDoc.RootElement.GetProperty("detail").GetString());
            Assert.Equal(creada1.NumeroRequisicion, problemDoc.RootElement.GetProperty("numeroRequisicion").GetString());

            // 3. Envío distinto (distinto insumo/cantidad o etapa) pasa y retorna 201 Created
            var payloadDistinto = new CrearRequisicionRequestDto
            {
                ObraId = obraId,
                Etapa = "Mampostería",
                Observaciones = "Segundo envío con otro material",
                Items = new List<CrearRequisicionItemDto>
                {
                    new() { ArticuloId = calSeedId, Cantidad = 15m }
                }
            };

            var respDistinta = await client.PostAsJsonAsync("/api/requisiciones", payloadDistinto);
            Assert.Equal(HttpStatusCode.Created, respDistinta.StatusCode);
            var creada2 = await respDistinta.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.NotNull(creada2);
            Assert.NotEqual(creada1.RequisicionId, creada2.RequisicionId);

            // En total existen exactamente 2 requisiciones en la obra (la duplicada no se creó)
            var listResp = await client.GetAsync($"/api/requisiciones?obraId={obraId}");
            var lista = await listResp.Content.ReadFromJsonAsync<List<RequisicionResponseDto>>();
            Assert.NotNull(lista);
            Assert.Equal(2, lista.Count);
        }

        [Fact]
        public async Task RechazarRequisicion_FlujoCompletoYValidacionesDeRolesMotivoEstadoYNotFound()
        {
            var capatazId = Guid.NewGuid();
            var compradorId = Guid.NewGuid();
            var adminId = Guid.NewGuid();
            var obraId = Guid.NewGuid();
            var cementoSeedId = Guid.Parse("11111111-1111-1111-1111-111111110001");
            var calSeedId = Guid.Parse("11111111-1111-1111-1111-111111110003");

            var client = _factory.CreateClient();

            // 1. Capataz crea dos requisiciones pendientes
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Capataz", capatazId));

            var respReq1 = await client.PostAsJsonAsync("/api/requisiciones", new CrearRequisicionRequestDto
            {
                ObraId = obraId,
                Etapa = "Estructura",
                Observaciones = "Requisición a rechazar por Comprador",
                Items = new List<CrearRequisicionItemDto>
                {
                    new() { ArticuloId = cementoSeedId, Cantidad = 120m }
                }
            });
            Assert.Equal(HttpStatusCode.Created, respReq1.StatusCode);
            var req1 = await respReq1.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.NotNull(req1);

            var respReq2 = await client.PostAsJsonAsync("/api/requisiciones", new CrearRequisicionRequestDto
            {
                ObraId = obraId,
                Etapa = "Terminaciones",
                Observaciones = "Requisición a rechazar por Admin",
                Items = new List<CrearRequisicionItemDto>
                {
                    new() { ArticuloId = calSeedId, Cantidad = 45m }
                }
            });
            Assert.Equal(HttpStatusCode.Created, respReq2.StatusCode);
            var req2 = await respReq2.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.NotNull(req2);

            // 2. Rol Capataz intentando rechazar recibe 403 Forbidden
            var forbiddenResp = await client.PostAsJsonAsync(
                $"/api/requisiciones/{req1.RequisicionId}/rechazar",
                new RechazarRequisicionRequestDto { Motivo = "Intento de capataz" });
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenResp.StatusCode);

            // 3. Rol Comprador intentando rechazar con motivo vacío recibe 400 Bad Request
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Comprador", compradorId));

            var badRequestResp = await client.PostAsJsonAsync(
                $"/api/requisiciones/{req1.RequisicionId}/rechazar",
                new RechazarRequisicionRequestDto { Motivo = "   " });
            Assert.Equal(HttpStatusCode.BadRequest, badRequestResp.StatusCode);

            // 4. Rol Comprador intentando rechazar un ID inexistente recibe 404 Not Found
            var notFoundResp = await client.PostAsJsonAsync(
                $"/api/requisiciones/{Guid.NewGuid()}/rechazar",
                new RechazarRequisicionRequestDto { Motivo = "Motivo válido" });
            Assert.Equal(HttpStatusCode.NotFound, notFoundResp.StatusCode);

            // 5. Rol Comprador rechaza req1 exitosamente -> 200 OK, estado Rechazada y fecha UTC con 'Z'
            var motivoComprador = "Cantidad excede el presupuesto asignado a Estructura.";
            var rechazar1Resp = await client.PostAsJsonAsync(
                $"/api/requisiciones/{req1.RequisicionId}/rechazar",
                new RechazarRequisicionRequestDto { Motivo = motivoComprador });
            Assert.Equal(HttpStatusCode.OK, rechazar1Resp.StatusCode);

            using var rechazar1Json = JsonDocument.Parse(await rechazar1Resp.Content.ReadAsStringAsync());
            Assert.EndsWith("Z", rechazar1Json.RootElement.GetProperty("fechaRechazo").GetString());

            var req1Rechazada = rechazar1Json.RootElement.Deserialize<RequisicionResponseDto>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.NotNull(req1Rechazada);
            Assert.Equal(EstadoRequisicion.Rechazada, req1Rechazada.Estado);
            Assert.Equal(compradorId, req1Rechazada.RechazadorId);
            Assert.Equal(motivoComprador, req1Rechazada.MotivoRechazo);
            Assert.NotNull(req1Rechazada.FechaRechazo);

            // 6. Intentar rechazar nuevamente una requisición ya rechazada -> 409 Conflict
            var conflictRechazadaResp = await client.PostAsJsonAsync(
                $"/api/requisiciones/{req1.RequisicionId}/rechazar",
                new RechazarRequisicionRequestDto { Motivo = "Rechazo duplicado" });
            Assert.Equal(HttpStatusCode.Conflict, conflictRechazadaResp.StatusCode);

            // 7. Rol Admin también puede rechazar (req2) -> 200 OK
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Admin", adminId));

            var motivoAdmin = "Insumo reemplazado por mezcla adhesiva en pliego técnico.";
            var rechazar2Resp = await client.PostAsJsonAsync(
                $"/api/requisiciones/{req2.RequisicionId}/rechazar",
                new RechazarRequisicionRequestDto { Motivo = motivoAdmin });
            Assert.Equal(HttpStatusCode.OK, rechazar2Resp.StatusCode);

            var req2Rechazada = await rechazar2Resp.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.NotNull(req2Rechazada);
            Assert.Equal(EstadoRequisicion.Rechazada, req2Rechazada.Estado);
            Assert.Equal(adminId, req2Rechazada.RechazadorId);
            Assert.Equal(motivoAdmin, req2Rechazada.MotivoRechazo);

            // 8. Verificar en GET /api/requisiciones que se refleje el estado Rechazada y su motivoRechazo
            var historialResp = await client.GetAsync($"/api/requisiciones?obraId={obraId}&estado=Rechazada");
            Assert.Equal(HttpStatusCode.OK, historialResp.StatusCode);
            var rechazadas = await historialResp.Content.ReadFromJsonAsync<List<RequisicionResponseDto>>();
            Assert.NotNull(rechazadas);
            Assert.Equal(2, rechazadas.Count);
            Assert.Contains(rechazadas, r => r.RequisicionId == req1.RequisicionId && r.MotivoRechazo == motivoComprador);
            Assert.Contains(rechazadas, r => r.RequisicionId == req2.RequisicionId && r.MotivoRechazo == motivoAdmin);
        }

        [Fact]
        public async Task Requisiciones_AislamientoPorCapataz_DosCapatacesDistintosYComprador_AislamientoEnListarYObtenerPorId()
        {
            var obraId = Guid.NewGuid();
            var capataz1Id = Guid.NewGuid();
            var capataz2Id = Guid.NewGuid();
            var compradorId = Guid.NewGuid();
            var adminId = Guid.NewGuid();
            var cementoSeedId = Guid.Parse("11111111-1111-1111-1111-111111110001");
            var calSeedId = Guid.Parse("11111111-1111-1111-1111-111111110003");

            var client = _factory.CreateClient();

            // 1. Capataz 1 crea Requisición 1
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Capataz", capataz1Id));

            var resp1 = await client.PostAsJsonAsync("/api/requisiciones", new CrearRequisicionRequestDto
            {
                ObraId = obraId,
                Etapa = "Fundaciones",
                Items = new List<CrearRequisicionItemDto>
                {
                    new() { ArticuloId = cementoSeedId, Cantidad = 50m }
                }
            });
            Assert.Equal(HttpStatusCode.Created, resp1.StatusCode);
            var req1 = await resp1.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.NotNull(req1);
            Assert.Equal(capataz1Id, req1.SolicitanteId);

            // 2. Capataz 2 crea Requisición 2
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Capataz", capataz2Id));

            var resp2 = await client.PostAsJsonAsync("/api/requisiciones", new CrearRequisicionRequestDto
            {
                ObraId = obraId,
                Etapa = "Mampostería",
                Items = new List<CrearRequisicionItemDto>
                {
                    new() { ArticuloId = calSeedId, Cantidad = 30m }
                }
            });
            Assert.Equal(HttpStatusCode.Created, resp2.StatusCode);
            var req2 = await resp2.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.NotNull(req2);
            Assert.Equal(capataz2Id, req2.SolicitanteId);

            // 3. Capataz 1 lista requisiciones: sólo debe ver las suyas (Requisición 1)
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Capataz", capataz1Id));

            var listarCapataz1Resp = await client.GetAsync($"/api/requisiciones?obraId={obraId}");
            Assert.Equal(HttpStatusCode.OK, listarCapataz1Resp.StatusCode);
            var listaCapataz1 = await listarCapataz1Resp.Content.ReadFromJsonAsync<List<RequisicionResponseDto>>();
            Assert.NotNull(listaCapataz1);
            Assert.Contains(listaCapataz1, r => r.RequisicionId == req1.RequisicionId);
            Assert.DoesNotContain(listaCapataz1, r => r.RequisicionId == req2.RequisicionId);
            Assert.All(listaCapataz1, r => Assert.Equal(capataz1Id, r.SolicitanteId));

            // 4. Capataz 2 lista requisiciones: sólo debe ver las suyas (Requisición 2)
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Capataz", capataz2Id));

            var listarCapataz2Resp = await client.GetAsync($"/api/requisiciones?obraId={obraId}");
            Assert.Equal(HttpStatusCode.OK, listarCapataz2Resp.StatusCode);
            var listaCapataz2 = await listarCapataz2Resp.Content.ReadFromJsonAsync<List<RequisicionResponseDto>>();
            Assert.NotNull(listaCapataz2);
            Assert.Contains(listaCapataz2, r => r.RequisicionId == req2.RequisicionId);
            Assert.DoesNotContain(listaCapataz2, r => r.RequisicionId == req1.RequisicionId);
            Assert.All(listaCapataz2, r => Assert.Equal(capataz2Id, r.SolicitanteId));

            // 5. Comprador lista requisiciones: debe ver ambas
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Comprador", compradorId));

            var listarCompradorResp = await client.GetAsync($"/api/requisiciones?obraId={obraId}");
            Assert.Equal(HttpStatusCode.OK, listarCompradorResp.StatusCode);
            var listaComprador = await listarCompradorResp.Content.ReadFromJsonAsync<List<RequisicionResponseDto>>();
            Assert.NotNull(listaComprador);
            Assert.Contains(listaComprador, r => r.RequisicionId == req1.RequisicionId);
            Assert.Contains(listaComprador, r => r.RequisicionId == req2.RequisicionId);

            // 6. Admin lista requisiciones: debe ver ambas
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Admin", adminId));

            var listarAdminResp = await client.GetAsync($"/api/requisiciones?obraId={obraId}");
            Assert.Equal(HttpStatusCode.OK, listarAdminResp.StatusCode);
            var listaAdmin = await listarAdminResp.Content.ReadFromJsonAsync<List<RequisicionResponseDto>>();
            Assert.NotNull(listaAdmin);
            Assert.Contains(listaAdmin, r => r.RequisicionId == req1.RequisicionId);
            Assert.Contains(listaAdmin, r => r.RequisicionId == req2.RequisicionId);

            // 7. Capataz 1 consulta por ID su propia requisición (req1) -> 200 OK
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Capataz", capataz1Id));

            var getReq1Capataz1Resp = await client.GetAsync($"/api/requisiciones/{req1.RequisicionId}");
            Assert.Equal(HttpStatusCode.OK, getReq1Capataz1Resp.StatusCode);
            var detalleReq1 = await getReq1Capataz1Resp.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.NotNull(detalleReq1);
            Assert.Equal(req1.RequisicionId, detalleReq1.RequisicionId);

            // 8. Capataz 1 consulta por ID la requisición ajena (req2) -> 404 Not Found (ProblemDetails)
            var getReq2Capataz1Resp = await client.GetAsync($"/api/requisiciones/{req2.RequisicionId}");
            Assert.Equal(HttpStatusCode.NotFound, getReq2Capataz1Resp.StatusCode);
            Assert.Equal("application/problem+json", getReq2Capataz1Resp.Content.Headers.ContentType?.MediaType);
            using (var jsonDoc = JsonDocument.Parse(await getReq2Capataz1Resp.Content.ReadAsStringAsync()))
            {
                Assert.Equal(404, jsonDoc.RootElement.GetProperty("status").GetInt32());
                Assert.Equal("Requisición no encontrada", jsonDoc.RootElement.GetProperty("title").GetString());
            }

            // 9. Capataz 2 consulta por ID su propia requisición (req2) -> 200 OK
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Capataz", capataz2Id));

            var getReq2Capataz2Resp = await client.GetAsync($"/api/requisiciones/{req2.RequisicionId}");
            Assert.Equal(HttpStatusCode.OK, getReq2Capataz2Resp.StatusCode);
            var detalleReq2 = await getReq2Capataz2Resp.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.NotNull(detalleReq2);
            Assert.Equal(req2.RequisicionId, detalleReq2.RequisicionId);

            // 10. Capataz 2 consulta por ID la requisición ajena (req1) -> 404 Not Found (ProblemDetails)
            var getReq1Capataz2Resp = await client.GetAsync($"/api/requisiciones/{req1.RequisicionId}");
            Assert.Equal(HttpStatusCode.NotFound, getReq1Capataz2Resp.StatusCode);
            Assert.Equal("application/problem+json", getReq1Capataz2Resp.Content.Headers.ContentType?.MediaType);

            // 11. Comprador consulta por ID ambas requisiciones -> 200 OK en ambas
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Comprador", compradorId));

            var getReq1CompradorResp = await client.GetAsync($"/api/requisiciones/{req1.RequisicionId}");
            Assert.Equal(HttpStatusCode.OK, getReq1CompradorResp.StatusCode);

            var getReq2CompradorResp = await client.GetAsync($"/api/requisiciones/{req2.RequisicionId}");
            Assert.Equal(HttpStatusCode.OK, getReq2CompradorResp.StatusCode);

            // 12. Admin consulta por ID ambas requisiciones -> 200 OK en ambas
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Admin", adminId));

            var getReq1AdminResp = await client.GetAsync($"/api/requisiciones/{req1.RequisicionId}");
            Assert.Equal(HttpStatusCode.OK, getReq1AdminResp.StatusCode);

            var getReq2AdminResp = await client.GetAsync($"/api/requisiciones/{req2.RequisicionId}");
            Assert.Equal(HttpStatusCode.OK, getReq2AdminResp.StatusCode);
        }
    }
}
