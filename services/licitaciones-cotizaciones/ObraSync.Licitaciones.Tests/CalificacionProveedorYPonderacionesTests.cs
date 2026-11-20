using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Domain.Entities;
using ObraSync.Licitaciones.Infrastructure;

namespace ObraSync.Licitaciones.Tests;

public class CalificacionProveedorYPonderacionesTests
{
    private const string TestSecret = "clave-de-pruebas-de-al-menos-32-caracteres";
    private const string TestIssuer = "obrasync-usuarios";
    private const string TestAudience = "obrasync-services";

    [Theory]
    [InlineData("Comprador", 5)]
    [InlineData("Admin", 1)]
    public async Task UpsertYGetCalificacionProveedor_ConRolAutorizado_Devuelve200OK(string rol, int calificacion)
    {
        var dbName = $"TestDb_CalifOk_{Guid.NewGuid()}";
        await using var factory = CreateFactory(dbName);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwt(rol));

        var proveedorId = Guid.NewGuid();
        var putResponse = await client.PutAsJsonAsync(
            $"/api/cotizaciones/proveedores/{proveedorId}/calificacion",
            new UpsertCalificacionProveedorRequestDto(calificacion));

        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        var bodyPut = await putResponse.Content.ReadFromJsonAsync<CalificacionProveedorResponseDto>();
        Assert.NotNull(bodyPut);
        Assert.Equal(proveedorId, bodyPut!.ProveedorId);
        Assert.Equal((decimal)calificacion, bodyPut.Calificacion);
        Assert.Equal(1, bodyPut.CantidadCalificaciones);

        var getResponse = await client.GetAsync($"/api/cotizaciones/proveedores/{proveedorId}/calificacion");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var bodyGet = await getResponse.Content.ReadFromJsonAsync<CalificacionProveedorResponseDto>();
        Assert.NotNull(bodyGet);
        Assert.Equal(proveedorId, bodyGet!.ProveedorId);
        Assert.Equal((decimal)calificacion, bodyGet.Calificacion);
    }

    [Fact]
    public async Task MismoUsuarioQueRecalifica_ActualizaSuCalificacionSinDuplicarFilas()
    {
        var dbName = $"TestDb_Recalif_{Guid.NewGuid()}";
        await using var factory = CreateFactory(dbName);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwt("Comprador"));

        var proveedorId = Guid.NewGuid();

        var r1 = await client.PutAsJsonAsync(
            $"/api/cotizaciones/proveedores/{proveedorId}/calificacion",
            new UpsertCalificacionProveedorRequestDto(2));
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);

        var r2 = await client.PutAsJsonAsync(
            $"/api/proveedores/{proveedorId}/calificacion",
            new UpsertCalificacionProveedorRequestDto(5));
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);

        var dto = await r2.Content.ReadFromJsonAsync<CalificacionProveedorResponseDto>();
        Assert.NotNull(dto);
        Assert.Equal(5m, dto!.Calificacion);
        Assert.Equal(1, dto.CantidadCalificaciones);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
        var totalFilas = await db.CalificacionesProveedores.CountAsync(x => x.ProveedorId == proveedorId);
        Assert.Equal(1, totalFilas);
    }

    [Fact]
    public async Task CalificacionesDeUsuariosDistintos_SePromedianYNoSePisan()
    {
        var dbName = $"TestDb_CalifPromedio_{Guid.NewGuid()}";
        await using var factory = CreateFactory(dbName);
        var proveedorId = Guid.NewGuid();

        // El comprador califica con 4 y después el admin (otro usuario) con 2: el proveedor queda en 3, no en 2.
        var comprador = factory.CreateClient();
        comprador.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwt("Comprador"));
        var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwt("Admin"));

        var r1 = await comprador.PutAsJsonAsync(
            $"/api/cotizaciones/proveedores/{proveedorId}/calificacion",
            new UpsertCalificacionProveedorRequestDto(4));
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);

        var r2 = await admin.PutAsJsonAsync(
            $"/api/cotizaciones/proveedores/{proveedorId}/calificacion",
            new UpsertCalificacionProveedorRequestDto(2));
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);

        var dto = await r2.Content.ReadFromJsonAsync<CalificacionProveedorResponseDto>();
        Assert.NotNull(dto);
        Assert.Equal(3m, dto!.Calificacion);
        Assert.Equal(2, dto.CantidadCalificaciones);

        var consulta = await comprador.GetFromJsonAsync<CalificacionProveedorResponseDto>(
            $"/api/cotizaciones/proveedores/{proveedorId}/calificacion");
        Assert.NotNull(consulta);
        Assert.Equal(3m, consulta!.Calificacion);
        Assert.Equal(2, consulta.CantidadCalificaciones);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
        Assert.Equal(2, await db.CalificacionesProveedores.CountAsync(x => x.ProveedorId == proveedorId));
    }

    [Fact]
    public async Task Promedio_SeRedondeaADosDecimales_YUnaRecalificacionCambiaSoloLaDeEseUsuario()
    {
        var dbName = $"TestDb_CalifRedondeo_{Guid.NewGuid()}";
        await using var factory = CreateFactory(dbName);
        var proveedorId = Guid.NewGuid();

        HttpClient ClienteDe(Guid usuario)
        {
            var c = factory.CreateClient();
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwt("Comprador", usuario));
            return c;
        }

        var u1 = ClienteDe(Guid.NewGuid());
        var u2 = ClienteDe(Guid.NewGuid());
        var u3 = ClienteDe(Guid.NewGuid());
        var url = $"/api/cotizaciones/proveedores/{proveedorId}/calificacion";

        await u1.PutAsJsonAsync(url, new UpsertCalificacionProveedorRequestDto(5));
        await u2.PutAsJsonAsync(url, new UpsertCalificacionProveedorRequestDto(4));
        var r3 = await u3.PutAsJsonAsync(url, new UpsertCalificacionProveedorRequestDto(4));
        var dto = await r3.Content.ReadFromJsonAsync<CalificacionProveedorResponseDto>();
        Assert.Equal(4.33m, dto!.Calificacion); // (5 + 4 + 4) / 3
        Assert.Equal(3, dto.CantidadCalificaciones);

        // El usuario 1 cambia su 5 por un 1: pesa solo su fila ((1 + 4 + 4) / 3 = 3).
        var r4 = await u1.PutAsJsonAsync(url, new UpsertCalificacionProveedorRequestDto(1));
        var dto2 = await r4.Content.ReadFromJsonAsync<CalificacionProveedorResponseDto>();
        Assert.Equal(3m, dto2!.Calificacion);
        Assert.Equal(3, dto2.CantidadCalificaciones);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(6)]
    public async Task UpsertCalificacionProveedor_FueraDeRango1A5_Devuelve400ProblemDetailsConCorrelationId(int calificacionInvalida)
    {
        var dbName = $"TestDb_CalifRango_{Guid.NewGuid()}";
        await using var factory = CreateFactory(dbName);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwt("Comprador"));
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "corr-calif-400");

        var proveedorId = Guid.NewGuid();
        var response = await client.PutAsJsonAsync(
            $"/api/cotizaciones/proveedores/{proveedorId}/calificacion",
            new UpsertCalificacionProveedorRequestDto(calificacionInvalida));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("corr-calif-400", json.RootElement.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task GetCalificacionProveedor_SinCalificacionPrevia_Devuelve404ProblemDetailsConCorrelationId()
    {
        var dbName = $"TestDb_Calif404_{Guid.NewGuid()}";
        await using var factory = CreateFactory(dbName);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwt("Admin"));
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "corr-calif-404");

        var response = await client.GetAsync($"/api/cotizaciones/proveedores/{Guid.NewGuid()}/calificacion");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(404, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("corr-calif-404", json.RootElement.GetProperty("correlationId").GetString());
    }

    [Theory]
    [InlineData("Capataz")]
    [InlineData("Proveedor")]
    public async Task EndpointsCalificacion_RolSinPermiso_Devuelve403Forbidden(string rol)
    {
        var dbName = $"TestDb_Calif403_{Guid.NewGuid()}";
        await using var factory = CreateFactory(dbName);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwt(rol));

        var proveedorId = Guid.NewGuid();
        var putResp = await client.PutAsJsonAsync(
            $"/api/cotizaciones/proveedores/{proveedorId}/calificacion",
            new UpsertCalificacionProveedorRequestDto(4));
        var getResp = await client.GetAsync($"/api/cotizaciones/proveedores/{proveedorId}/calificacion");

        Assert.Equal(HttpStatusCode.Forbidden, putResp.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, getResp.StatusCode);
    }

    [Fact]
    public async Task EndpointsCalificacion_SinToken_Devuelve401Unauthorized()
    {
        var dbName = $"TestDb_Calif401_{Guid.NewGuid()}";
        await using var factory = CreateFactory(dbName);
        var client = factory.CreateClient();

        var proveedorId = Guid.NewGuid();
        var putResp = await client.PutAsJsonAsync(
            $"/api/cotizaciones/proveedores/{proveedorId}/calificacion",
            new UpsertCalificacionProveedorRequestDto(4));
        var getResp = await client.GetAsync($"/api/cotizaciones/proveedores/{proveedorId}/calificacion");

        Assert.Equal(HttpStatusCode.Unauthorized, putResp.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, getResp.StatusCode);
    }

    [Fact]
    public async Task Comparativa_ReflejaCalificacionProveedorPenalizaNoCalificadoYCambiaGanadoraAlModificarPesos()
    {
        var dbName = $"TestDb_ComparativaPesos_{Guid.NewGuid()}";
        await using var factory = CreateFactory(dbName);

        var licitacionId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        var provBaratoSinCalif = Guid.NewGuid();
        var provCaroMejorCalif = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
            db.Licitaciones.Add(new Licitacion
            {
                LicitacionId = licitacionId,
                ObraId = Guid.NewGuid(),
                RequisicionId = Guid.NewGuid(),
                NumeroRequisicion = "REQ-88",
                Estado = EstadoLicitacion.Abierta,
                FechaCreacion = DateTime.UtcNow,
                FechaLimiteCotizacion = DateTime.UtcNow.AddDays(5),
                Items = new List<LicitacionItem>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        LicitacionId = licitacionId,
                        ArticuloId = articuloId,
                        Descripcion = "Hierro 12mm",
                        Cantidad = 10m,
                        UnidadMedida = "barra"
                    }
                }
            });

            // Oferta A: más barata (precio total 1000) pero proveedor sin calificar
            var cotizAId = Guid.NewGuid();
            db.Cotizaciones.Add(new Cotizacion
            {
                CotizacionId = cotizAId,
                LicitacionId = licitacionId,
                ProveedorId = provBaratoSinCalif,
                Flete = 50m,
                PlazoEntregaDias = 5,
                FechaCreacion = DateTime.UtcNow,
                Items = new List<CotizacionItem>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        CotizacionId = cotizAId,
                        ArticuloId = articuloId,
                        PrecioUnitario = 100m,
                        CantidadOfrecida = 10m
                    }
                }
            });

            // Oferta B: un poco más cara (precio total 1100) pero proveedor calificado con 5
            var cotizBId = Guid.NewGuid();
            db.Cotizaciones.Add(new Cotizacion
            {
                CotizacionId = cotizBId,
                LicitacionId = licitacionId,
                ProveedorId = provCaroMejorCalif,
                Flete = 50m,
                PlazoEntregaDias = 5,
                FechaCreacion = DateTime.UtcNow,
                Items = new List<CotizacionItem>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        CotizacionId = cotizBId,
                        ArticuloId = articuloId,
                        PrecioUnitario = 110m,
                        CantidadOfrecida = 10m
                    }
                }
            });

            await db.SaveChangesAsync();
        }

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwt("Comprador"));

        // Calificamos solo al segundo proveedor con 5
        var putResp = await client.PutAsJsonAsync(
            $"/api/cotizaciones/proveedores/{provCaroMejorCalif}/calificacion",
            new UpsertCalificacionProveedorRequestDto(5));
        Assert.Equal(HttpStatusCode.OK, putResp.StatusCode);

        // 1) Con peso alto en precio (pesoPrecio=90, pesoCalificacion=0), gana el proveedor más barato sin calificar
        var respPrecio = await client.GetFromJsonAsync<List<CotizacionResponseDto>>(
            $"/api/cotizaciones?licitacionId={licitacionId}&pesoPrecio=90&pesoFlete=5&pesoPlazo=5&pesoCalificacion=0");
        Assert.NotNull(respPrecio);
        Assert.Equal(2, respPrecio!.Count);

        var ofertaSinCalifEnPrecio = respPrecio.Single(c => c.ProveedorId == provBaratoSinCalif);
        var ofertaCalificadaEnPrecio = respPrecio.Single(c => c.ProveedorId == provCaroMejorCalif);

        Assert.True(ofertaSinCalifEnPrecio.EsGanadora);
        Assert.False(ofertaCalificadaEnPrecio.EsGanadora);
        Assert.Null(ofertaSinCalifEnPrecio.CalificacionProveedor);
        Assert.Equal(0m, ofertaSinCalifEnPrecio.DesgloseScoring!.PuntajeCalificacion);

        Assert.Equal(5m, ofertaCalificadaEnPrecio.CalificacionProveedor);
        Assert.Equal(100m, ofertaCalificadaEnPrecio.DesgloseScoring!.PuntajeCalificacion);

        // 2) Al cambiar los pesos por query para priorizar calificación (pesoPrecio=10, pesoCalificacion=70), cambia la ganadora
        var respCalif = await client.GetFromJsonAsync<List<CotizacionResponseDto>>(
            $"/api/cotizaciones?licitacionId={licitacionId}&pesoPrecio=10&pesoFlete=10&pesoPlazo=10&pesoCalificacion=70");
        Assert.NotNull(respCalif);

        var ofertaSinCalifEnCalif = respCalif!.Single(c => c.ProveedorId == provBaratoSinCalif);
        var ofertaCalificadaEnCalif = respCalif.Single(c => c.ProveedorId == provCaroMejorCalif);

        Assert.True(ofertaCalificadaEnCalif.EsGanadora);
        Assert.False(ofertaSinCalifEnCalif.EsGanadora);
        Assert.Equal(0.70m, ofertaCalificadaEnCalif.DesgloseScoring!.PesoCalificacion);
        Assert.Equal(30m, ofertaSinCalifEnCalif.PuntajeTotal); // penalizado: 0 en el 70% de calificación
    }

    [Theory]
    [InlineData("pesoPrecio=-0.1&pesoFlete=0.2&pesoPlazo=0.2&pesoCalificacion=0.1")]
    [InlineData("pesoPrecio=0&pesoFlete=0&pesoPlazo=0&pesoCalificacion=0")]
    public async Task Comparativa_PesosInvalidosEnQuery_Devuelve400ProblemDetails(string queryPesos)
    {
        var dbName = $"TestDb_PesosInvalidos_{Guid.NewGuid()}";
        await using var factory = CreateFactory(dbName);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwt("Comprador"));
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "corr-pesos-400");

        var licitacionId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
            db.Licitaciones.Add(new Licitacion
            {
                LicitacionId = licitacionId,
                ObraId = Guid.NewGuid(),
                RequisicionId = Guid.NewGuid(),
                NumeroRequisicion = "REQ-88-ERR",
                Estado = EstadoLicitacion.Abierta,
                FechaCreacion = DateTime.UtcNow,
                FechaLimiteCotizacion = DateTime.UtcNow.AddDays(3)
            });
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/cotizaciones?licitacionId={licitacionId}&{queryPesos}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("corr-pesos-400", json.RootElement.GetProperty("correlationId").GetString());
    }

    private static WebApplicationFactory<Program> CreateFactory(string dbName)
    {
        Environment.SetEnvironmentVariable("Jwt__Secret", TestSecret);
        Environment.SetEnvironmentVariable("RabbitMQ__UseInMemory", "true");

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:Secret", TestSecret);
            builder.UseSetting("Jwt:Issuer", TestIssuer);
            builder.UseSetting("Jwt:Audience", TestAudience);
            builder.UseSetting("RabbitMQ:UseInMemory", "true");

            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<LicitacionesDbContext>));
                services.RemoveAll(typeof(IDbContextOptionsConfiguration<LicitacionesDbContext>));

                services.AddDbContext<LicitacionesDbContext>(options =>
                    options.UseInMemoryDatabase(dbName)
                        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
            });
        });
    }

    private static string GenerateJwt(string role, Guid? userId = null)
    {
        var claims = new[]
        {
            new Claim("sub", (userId ?? Guid.NewGuid()).ToString()),
            new Claim("email", $"{role.ToLowerInvariant()}@obrasync.com"),
            new Claim("name", $"Usuario {role}"),
            new Claim("role", role)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: TestIssuer,
            audience: TestAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
