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
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Domain.Entities;
using ObraSync.Usuarios.Infrastructure.Data;
using Xunit;

namespace ObraSync.Usuarios.Tests;

public class ObrasEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestJwtSecret = "ClaveSecretaDePruebaParaUsuariosYObrasMinimo32Bytes!";
    private readonly WebApplicationFactory<Program> _factory;

    public ObrasEndpointTests(WebApplicationFactory<Program> factory)
    {
        var dbName = $"DB_Obras_Endpoint_{Guid.NewGuid()}";

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:Secret", TestJwtSecret);
            builder.UseSetting("ConnectionStrings:DefaultConnection", "InMemoryTestDb");

            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<AuthDbContext>));

                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<AuthDbContext>(options =>
                {
                    options.UseInMemoryDatabase(dbName);
                });
            });
        });
    }

    private static string GenerateJwtToken(string role, Guid? userId = null, string email = "test@obrasync.com")
        => GenerateJwtTokenConSub((userId ?? Guid.NewGuid()).ToString(), email, role);

    // Permite un 'sub' arbitrario (ej. no-GUID) y varios roles en el mismo token (#155).
    private static string GenerateJwtTokenConSub(string sub, string email, params string[] roles)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new Claim("sub", sub),
            new Claim("email", email),
            new Claim("name", $"Usuario {string.Join("/", roles)}")
        };
        claims.AddRange(roles.Select(r => new Claim("role", r)));

        var token = new JwtSecurityToken(
            issuer: "obrasync-usuarios",
            audience: "obrasync-services",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // Id del capataz de prueba sembrado al arrancar (capataz@obrasync.com), dueño de las 3 obras de ejemplo.
    private async Task<Guid> ObtenerIdCapatazSembradoAsync()
    {
        _factory.CreateClient(); // fuerza el arranque del host (y por lo tanto el sembrado)
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        return await context.Users
            .Where(u => u.Email == "capataz@obrasync.com")
            .Select(u => u.Id)
            .SingleAsync();
    }

    // Crea un segundo capataz con una obra activa asignada a él, para probar el aislamiento entre capataces.
    private async Task<(Guid CapatazId, Guid ObraId)> CrearOtroCapatazConObraAsync()
    {
        _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        var email = $"capataz.b.{Guid.NewGuid():N}@obrasync.com";
        var capatazB = new AppUser
        {
            UserName = email,
            Email = email,
            NombreCompleto = "Beatriz Capataz",
            EmailConfirmed = true
        };
        var resultado = await userManager.CreateAsync(capatazB, "Password123!");
        Assert.True(resultado.Succeeded, string.Join(", ", resultado.Errors.Select(e => e.Description)));
        await userManager.AddToRoleAsync(capatazB, UserRoles.Capataz);

        var obraB = new Obra
        {
            Nombre = $"Obra de Beatriz {Guid.NewGuid():N}"[..24],
            Ubicacion = "Calle del Capataz B 456, Quilmes",
            CapatazId = capatazB.Id,
            Activa = true,
            Etapas = new List<EtapaObra> { new EtapaObra { NombreEtapa = "Excavación" } }
        };
        context.Obras.Add(obraB);
        await context.SaveChangesAsync();

        return (capatazB.Id, obraB.Id);
    }

    [Fact]
    public async Task GetObras_SinToken_DebeRetornar401Unauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/Obras");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetObras_ConRolNoAutorizado_Proveedor_DebeRetornar403Forbidden()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateJwtToken(UserRoles.Proveedor));

        var response = await client.GetAsync("/api/Obras");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(UserRoles.Capataz)]
    [InlineData(UserRoles.Comprador)]
    [InlineData(UserRoles.Admin)]
    public async Task GetObras_ConRolesAutorizados_DebeRetornar200ConObrasEtapasYCapatazId(string role)
    {
        // #155: el Capataz solo ve sus obras, así que se usa el capataz sembrado (dueño de las obras de ejemplo).
        var capatazSembradoId = await ObtenerIdCapatazSembradoAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateJwtToken(role,
                role == UserRoles.Capataz ? capatazSembradoId : null));

        var response = await client.GetAsync("/api/Obras");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var obras = await response.Content.ReadFromJsonAsync<List<ObraResponseDto>>();
        Assert.NotNull(obras);
        Assert.True(obras.Count >= 3, "Deben existir al menos las 3 obras sembradas.");

        foreach (var obra in obras)
        {
            Assert.NotEqual(Guid.Empty, obra.Id);
            Assert.False(string.IsNullOrWhiteSpace(obra.Nombre));
            Assert.False(string.IsNullOrWhiteSpace(obra.Ubicacion));
            Assert.NotEqual(default, obra.FechaInicio);
            Assert.True(obra.Activa);

            // Requisito #61: Debe incluir CapatazId y Capataz
            Assert.NotEqual(Guid.Empty, obra.CapatazId);
            Assert.Equal("Carlos Capataz", obra.Capataz);

            // Requisito #61: Cada obra debe incluir sus etapas constructivas
            Assert.NotNull(obra.Etapas);
            Assert.True(obra.Etapas.Count >= 3, $"La obra '{obra.Nombre}' debe contener al menos 3 etapas.");

            foreach (var etapa in obra.Etapas)
            {
                Assert.NotEqual(Guid.Empty, etapa.Id);
                Assert.False(string.IsNullOrWhiteSpace(etapa.NombreEtapa));
                Assert.Equal("Pendiente", etapa.Estado);
            }
        }
    }

    // ── #155: el Capataz ve solo sus obras (filtro en el backend) ─────────────────────────

    [Fact]
    public async Task GetObras_CapatazA_NoVeLasObrasDelCapatazB_YViceversa()
    {
        var capatazAId = await ObtenerIdCapatazSembradoAsync();
        var (capatazBId, obraBId) = await CrearOtroCapatazConObraAsync();
        var client = _factory.CreateClient();

        // Capataz A: ve sus 3 obras sembradas y ninguna de B
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateJwtToken(UserRoles.Capataz, capatazAId));
        var respA = await client.GetAsync("/api/Obras");
        Assert.Equal(HttpStatusCode.OK, respA.StatusCode);
        var obrasA = await respA.Content.ReadFromJsonAsync<List<ObraResponseDto>>();
        Assert.NotNull(obrasA);
        Assert.True(obrasA.Count >= 3);
        Assert.All(obrasA, o => Assert.Equal(capatazAId, o.CapatazId));
        Assert.DoesNotContain(obrasA, o => o.Id == obraBId);

        // Capataz B: ve solo la suya
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateJwtToken(UserRoles.Capataz, capatazBId));
        var respB = await client.GetAsync("/api/Obras");
        Assert.Equal(HttpStatusCode.OK, respB.StatusCode);
        var obrasB = await respB.Content.ReadFromJsonAsync<List<ObraResponseDto>>();
        Assert.NotNull(obrasB);
        var unica = Assert.Single(obrasB);
        Assert.Equal(obraBId, unica.Id);
        Assert.Equal(capatazBId, unica.CapatazId);
        Assert.Equal("Beatriz Capataz", unica.Capataz);
    }

    [Theory]
    [InlineData(UserRoles.Comprador)]
    [InlineData(UserRoles.Admin)]
    public async Task GetObras_CompradorYAdmin_VenLasObrasDeTodosLosCapataces(string role)
    {
        var capatazAId = await ObtenerIdCapatazSembradoAsync();
        var (capatazBId, obraBId) = await CrearOtroCapatazConObraAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateJwtToken(role));

        var response = await client.GetAsync("/api/Obras");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var obras = await response.Content.ReadFromJsonAsync<List<ObraResponseDto>>();
        Assert.NotNull(obras);
        Assert.Contains(obras, o => o.CapatazId == capatazAId);
        Assert.Contains(obras, o => o.Id == obraBId && o.CapatazId == capatazBId);
    }

    [Fact]
    public async Task GetObras_CapatazSinObrasAsignadas_DebeRetornar200ConListaVacia()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateJwtToken(UserRoles.Capataz, Guid.NewGuid()));

        var response = await client.GetAsync("/api/Obras");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var obras = await response.Content.ReadFromJsonAsync<List<ObraResponseDto>>();
        Assert.NotNull(obras);
        Assert.Empty(obras);
    }

    [Fact]
    public async Task GetObras_CapatazConSubInvalido_DebeRetornar401ConProblemDetails()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            GenerateJwtTokenConSub("no-es-un-guid", "capataz@obrasync.com", UserRoles.Capataz));

        var response = await client.GetAsync("/api/Obras");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("No autorizado", problem.Title);
        Assert.Contains("claim 'sub' válido", problem.Detail);
    }

    [Fact]
    public async Task GetObras_UsuarioCapatazYAdmin_ConservaLaVistaCompleta()
    {
        await ObtenerIdCapatazSembradoAsync();
        var (_, obraBId) = await CrearOtroCapatazConObraAsync();
        var client = _factory.CreateClient();
        // sub sin obras asignadas: si se filtrara como Capataz, la lista vendría vacía
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            GenerateJwtTokenConSub(Guid.NewGuid().ToString(), "jefe@obrasync.com", UserRoles.Capataz, UserRoles.Admin));

        var response = await client.GetAsync("/api/Obras");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var obras = await response.Content.ReadFromJsonAsync<List<ObraResponseDto>>();
        Assert.NotNull(obras);
        Assert.True(obras.Count >= 4);
        Assert.Contains(obras, o => o.Id == obraBId);
    }

    [Fact]
    public async Task PostObra_ConAdmin_DebeResponder201CreatedYReflejarseEnGetObrasConEtapasYCapatazId()
    {
        var client = _factory.CreateClient();

        // 1. Obtener capataz sembrado
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateJwtToken(UserRoles.Admin));

        var obrasInicialesResp = await client.GetAsync("/api/Obras");
        Assert.Equal(HttpStatusCode.OK, obrasInicialesResp.StatusCode);
        var obrasIniciales = await obrasInicialesResp.Content.ReadFromJsonAsync<List<ObraResponseDto>>();
        Assert.NotNull(obrasIniciales);
        var capatazId = obrasIniciales.First().CapatazId;

        // 2. Crear nueva obra vía POST /api/Obras
        var nuevoNombre = $"Obra Test {Guid.NewGuid():N}"[..18];
        var crearDto = new CrearObraDto
        {
            Nombre = nuevoNombre,
            Ubicacion = "Av. Santa Fe 1234, CABA",
            FechaInicio = DateTime.UtcNow,
            CapatazId = capatazId,
            FasesEtapas = new List<string> { "Excavación", "Fundaciones", "Estructura", "Cubiertas" }
        };

        var postResponse = await client.PostAsJsonAsync("/api/Obras", crearDto);
        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);

        var createdResult = await postResponse.Content.ReadFromJsonAsync<CrearObraResponseDto>();
        Assert.NotNull(createdResult);
        Assert.NotEqual(Guid.Empty, createdResult.Id);
        Assert.Equal("Obra creada exitosamente", createdResult.Mensaje);

        // 3. Consultar vía GET /api/Obras como el Capataz asignado y verificar que contenga la nueva obra con sus etapas y capatazId
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateJwtToken(UserRoles.Capataz, capatazId));

        var getResponse = await client.GetAsync("/api/Obras");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var obras = await getResponse.Content.ReadFromJsonAsync<List<ObraResponseDto>>();
        Assert.NotNull(obras);

        var obraCreada = obras.FirstOrDefault(o => o.Id == createdResult.Id);
        Assert.NotNull(obraCreada);
        Assert.Equal(nuevoNombre, obraCreada.Nombre);
        Assert.Equal(capatazId, obraCreada.CapatazId);
        Assert.Equal("Carlos Capataz", obraCreada.Capataz);
        Assert.True(obraCreada.Activa);
        Assert.Equal(4, obraCreada.Etapas.Count);
        Assert.Equal(new[] { "Excavación", "Fundaciones", "Estructura", "Cubiertas" },
            obraCreada.Etapas.Select(e => e.NombreEtapa).ToArray());
    }

    [Fact]
    public async Task PostObra_DuplicadaConObraActiva_DebeRetornar409ConflictYProblemDetails()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateJwtToken(UserRoles.Admin));

        var getResp = await client.GetAsync("/api/Obras");
        var obras = await getResp.Content.ReadFromJsonAsync<List<ObraResponseDto>>();
        var obraExistente = obras!.First();

        var crearDto = new CrearObraDto
        {
            Nombre = $"  {obraExistente.Nombre.ToUpper()}  ",
            Ubicacion = $"  {obraExistente.Ubicacion.ToLower()}  ",
            CapatazId = obraExistente.CapatazId,
            FasesEtapas = new List<string> { "Etapa A" }
        };

        var postResp = await client.PostAsJsonAsync("/api/Obras", crearDto);
        Assert.Equal(HttpStatusCode.Conflict, postResp.StatusCode);

        var problem = await postResp.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(409, problem.Status);
        Assert.Equal("Conflicto en la operación", problem.Title);
        Assert.Contains("Ya existe una obra activa con el mismo nombre y ubicación.", problem.Detail);
    }

    [Fact]
    public async Task PostObra_DiferenteNombreMismaUbicacion_DebeResponder201Created()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateJwtToken(UserRoles.Admin));

        var getResp = await client.GetAsync("/api/Obras");
        var obras = await getResp.Content.ReadFromJsonAsync<List<ObraResponseDto>>();
        var obraExistente = obras!.First();

        var crearDto = new CrearObraDto
        {
            Nombre = $"Obra Distinta {Guid.NewGuid():N}"[..20],
            Ubicacion = obraExistente.Ubicacion,
            CapatazId = obraExistente.CapatazId,
            FasesEtapas = new List<string> { "Etapa 1" }
        };

        var postResp = await client.PostAsJsonAsync("/api/Obras", crearDto);
        Assert.Equal(HttpStatusCode.Created, postResp.StatusCode);
    }

    [Fact]
    public async Task PostObra_MismoNombreDiferenteUbicacion_DebeResponder201Created()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateJwtToken(UserRoles.Admin));

        var getResp = await client.GetAsync("/api/Obras");
        var obras = await getResp.Content.ReadFromJsonAsync<List<ObraResponseDto>>();
        var obraExistente = obras!.First();

        var crearDto = new CrearObraDto
        {
            Nombre = obraExistente.Nombre,
            Ubicacion = $"Otra Direccion {Guid.NewGuid():N}"[..25],
            CapatazId = obraExistente.CapatazId,
            FasesEtapas = new List<string> { "Etapa 1" }
        };

        var postResp = await client.PostAsJsonAsync("/api/Obras", crearDto);
        Assert.Equal(HttpStatusCode.Created, postResp.StatusCode);
    }

    [Fact]
    public async Task PostObra_MismoNombreYUbicacionQueObraDadaDeBaja_DebeResponder201Created()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateJwtToken(UserRoles.Admin));

        var getResp = await client.GetAsync("/api/Obras");
        var obras = await getResp.Content.ReadFromJsonAsync<List<ObraResponseDto>>();
        var capatazId = obras!.First().CapatazId;

        // 1. Crear una obra inicial
        var nombreUnico = $"Obra Baja Test {Guid.NewGuid():N}"[..22];
        var ubicacionUnica = "Av. Siempre Viva 742, Springfield";

        var post1 = await client.PostAsJsonAsync("/api/Obras", new CrearObraDto
        {
            Nombre = nombreUnico,
            Ubicacion = ubicacionUnica,
            CapatazId = capatazId,
            FasesEtapas = new List<string> { "Inicio" }
        });
        Assert.Equal(HttpStatusCode.Created, post1.StatusCode);
        var created = await post1.Content.ReadFromJsonAsync<CrearObraResponseDto>();

        // 2. Darla de baja lógica
        var deleteResp = await client.DeleteAsync($"/api/Obras/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        // 3. Crear una nueva obra con el mismo nombre y ubicación (debe permitirse porque la anterior está inactiva)
        var post2 = await client.PostAsJsonAsync("/api/Obras", new CrearObraDto
        {
            Nombre = $"  {nombreUnico.ToUpper()}  ",
            Ubicacion = $"  {ubicacionUnica.ToLower()}  ",
            CapatazId = capatazId,
            FasesEtapas = new List<string> { "Fase Nueva" }
        });
        Assert.Equal(HttpStatusCode.Created, post2.StatusCode);
    }

    [Fact]
    public async Task PutObra_SinToken_DebeRetornar401Unauthorized()
    {
        var client = _factory.CreateClient();
        var dto = new ActualizarObraDto { Nombre = "Cualquiera", CapatazId = Guid.NewGuid() };

        var response = await client.PutAsJsonAsync($"/api/Obras/{Guid.NewGuid()}", dto);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(UserRoles.Proveedor)]
    [InlineData(UserRoles.Capataz)]
    public async Task PutObra_ConRolesNoAutorizados_DebeRetornar403Forbidden(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateJwtToken(role));

        var dto = new ActualizarObraDto { Nombre = "Cualquiera", CapatazId = Guid.NewGuid() };

        var response = await client.PutAsJsonAsync($"/api/Obras/{Guid.NewGuid()}", dto);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(UserRoles.Admin)]
    [InlineData(UserRoles.Comprador)]
    public async Task PutObra_ObraInexistente_DebeRetornar404NotFound(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateJwtToken(role));

        var getResp = await client.GetAsync("/api/Obras");
        var obras = await getResp.Content.ReadFromJsonAsync<List<ObraResponseDto>>();
        var capatazId = obras!.First().CapatazId;

        var dto = new ActualizarObraDto
        {
            Nombre = "Obra Fantasma",
            Ubicacion = "En ninguna parte 123",
            FechaInicio = DateTime.UtcNow,
            CapatazId = capatazId
        };

        var response = await client.PutAsJsonAsync($"/api/Obras/{Guid.NewGuid()}", dto);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(UserRoles.Admin)]
    [InlineData(UserRoles.Comprador)]
    public async Task PutObra_CapatazInexistente_DebeRetornar400BadRequest(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateJwtToken(role));

        var getResp = await client.GetAsync("/api/Obras");
        var obras = await getResp.Content.ReadFromJsonAsync<List<ObraResponseDto>>();
        var obraExistente = obras!.First();

        var dto = new ActualizarObraDto
        {
            Nombre = obraExistente.Nombre,
            Ubicacion = obraExistente.Ubicacion,
            FechaInicio = obraExistente.FechaInicio,
            CapatazId = Guid.NewGuid() // Capataz inexistente
        };

        var response = await client.PutAsJsonAsync($"/api/Obras/{obraExistente.Id}", dto);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(UserRoles.Admin)]
    [InlineData(UserRoles.Comprador)]
    public async Task PutObra_DuplicadaConOtraObraActiva_DebeRetornar409Conflict(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateJwtToken(role));

        var getResp = await client.GetAsync("/api/Obras");
        var obras = await getResp.Content.ReadFromJsonAsync<List<ObraResponseDto>>();
        Assert.True(obras!.Count >= 2);

        var obraDestino = obras[0];
        var obraOtra = obras[1];

        // Intentar actualizar obraDestino para que tenga el mismo nombre y ubicación de obraOtra
        // (con variaciones de mayúsculas y espacios para validar regla insensible y trimmed)
        var dto = new ActualizarObraDto
        {
            Nombre = $"  {obraOtra.Nombre.ToUpper()}  ",
            Ubicacion = $"  {obraOtra.Ubicacion.ToLower()}  ",
            FechaInicio = DateTime.UtcNow,
            CapatazId = obraDestino.CapatazId
        };

        var response = await client.PutAsJsonAsync($"/api/Obras/{obraDestino.Id}", dto);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData(UserRoles.Admin)]
    [InlineData(UserRoles.Comprador)]
    public async Task PutObra_ConAdminOComprador_Exitoso_DebeRetornar200ConDatosActualizados(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateJwtToken(role));

        var getResp = await client.GetAsync("/api/Obras");
        var obras = await getResp.Content.ReadFromJsonAsync<List<ObraResponseDto>>();
        var obraOriginal = obras!.First(o => o.Nombre == "Torre Alvear");

        var nuevoNombre = $"Torre Alvear Renombrada {Guid.NewGuid():N}"[..25];
        var nuevaUbicacion = "Av. Alvear 1900, Piso 5, CABA";
        var nuevaFecha = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);

        var dto = new ActualizarObraDto
        {
            Nombre = nuevoNombre,
            Ubicacion = nuevaUbicacion,
            FechaInicio = nuevaFecha,
            CapatazId = obraOriginal.CapatazId
        };

        var putResp = await client.PutAsJsonAsync($"/api/Obras/{obraOriginal.Id}", dto);

        Assert.Equal(HttpStatusCode.OK, putResp.StatusCode);
        var obraActualizada = await putResp.Content.ReadFromJsonAsync<ObraResponseDto>();

        Assert.NotNull(obraActualizada);
        Assert.Equal(obraOriginal.Id, obraActualizada.Id);
        Assert.Equal(nuevoNombre, obraActualizada.Nombre);
        Assert.Equal(nuevaUbicacion, obraActualizada.Ubicacion);
        Assert.Equal(nuevaFecha, obraActualizada.FechaInicio);
        Assert.Equal(obraOriginal.CapatazId, obraActualizada.CapatazId);
        Assert.True(obraActualizada.Activa);
        Assert.NotEmpty(obraActualizada.Etapas);
    }

    [Fact]
    public async Task DeleteObra_SinToken_DebeRetornar401Unauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.DeleteAsync($"/api/Obras/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(UserRoles.Proveedor)]
    [InlineData(UserRoles.Capataz)]
    public async Task DeleteObra_ConRolesNoAutorizados_DebeRetornar403Forbidden(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateJwtToken(role));

        var response = await client.DeleteAsync($"/api/Obras/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(UserRoles.Admin)]
    [InlineData(UserRoles.Comprador)]
    public async Task DeleteObra_ObraInexistente_DebeRetornar404NotFound(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateJwtToken(role));

        var response = await client.DeleteAsync($"/api/Obras/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(UserRoles.Admin)]
    [InlineData(UserRoles.Comprador)]
    public async Task DeleteObra_ConAdminOComprador_DebeRetornar204NoContent_YNoAparecerEnGetObras(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateJwtToken(role));

        // 1. Crear una obra para dar de baja
        var getResp = await client.GetAsync("/api/Obras");
        var obras = await getResp.Content.ReadFromJsonAsync<List<ObraResponseDto>>();
        var capatazId = obras!.First().CapatazId;

        var nombreObra = $"Obra Para Borrar {Guid.NewGuid():N}"[..22];
        var postResp = await client.PostAsJsonAsync("/api/Obras", new CrearObraDto
        {
            Nombre = nombreObra,
            Ubicacion = "Calle Falsa 123",
            CapatazId = capatazId,
            FasesEtapas = new List<string> { "Etapa 1" }
        });
        Assert.Equal(HttpStatusCode.Created, postResp.StatusCode);
        var created = await postResp.Content.ReadFromJsonAsync<CrearObraResponseDto>();

        // 2. Dar de baja lógica
        var deleteResp = await client.DeleteAsync($"/api/Obras/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        // 3. Verificar que ya no aparece en GET /api/Obras
        var getRespDespues = await client.GetAsync("/api/Obras");
        var obrasDespues = await getRespDespues.Content.ReadFromJsonAsync<List<ObraResponseDto>>();
        Assert.DoesNotContain(obrasDespues!, o => o.Id == created.Id);

        // 4. Intentar dar de baja nuevamente -> debe responder 404 Not Found porque ya está inactiva
        var reDeleteResp = await client.DeleteAsync($"/api/Obras/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, reDeleteResp.StatusCode);

        // 5. Intentar editar la obra inactiva -> debe responder 404 Not Found
        var putInactivaResp = await client.PutAsJsonAsync($"/api/Obras/{created.Id}", new ActualizarObraDto
        {
            Nombre = "Intento Editar Inactiva",
            CapatazId = capatazId
        });
        Assert.Equal(HttpStatusCode.NotFound, putInactivaResp.StatusCode);
    }
}
