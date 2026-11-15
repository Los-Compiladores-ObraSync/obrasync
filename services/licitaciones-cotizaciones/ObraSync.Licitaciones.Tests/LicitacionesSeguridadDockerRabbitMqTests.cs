using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ObraSync.Licitaciones.Tests;

/// <summary>
/// Pruebas de seguridad de la tarjeta #169 para Licitaciones-Cotizaciones:
/// 1. El servicio falla al arrancar con InvalidOperationException si faltan las credenciales de RabbitMQ
///    (RabbitMQ:Username / RabbitMQ:Password) cuando no se usa el transporte en memoria (sin fallback a "guest").
/// 2. El Dockerfile de licitaciones-cotizaciones ejecuta la etapa final con un usuario no-root (USER $APP_UID).
/// </summary>
public class LicitacionesSeguridadDockerRabbitMqTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    [InlineData("usuario-valido", "")]
    [InlineData("", "password-valida")]
    public void Arrancar_SinCredencialesRabbitMq_LanzaInvalidOperationException(string? username, string? password)
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Jwt:Secret", LicitacionesApiFactory.Secret);
                builder.UseSetting("RabbitMQ:UseInMemory", "false");
                builder.UseSetting("RabbitMQ:Username", username);
                builder.UseSetting("RabbitMQ:Password", password);
            });

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        var invalidOp = ex as InvalidOperationException ?? ex.InnerException as InvalidOperationException;

        Assert.NotNull(invalidOp);
        Assert.Contains("RabbitMQ:Username", invalidOp.Message);
        Assert.Contains("RabbitMQ:Password", invalidOp.Message);
    }

    [Fact]
    public void Dockerfile_DefineUsuarioNoRootEnEtapaFinal()
    {
        var directorioActual = new DirectoryInfo(AppContext.BaseDirectory);
        while (directorioActual != null && !File.Exists(Path.Combine(directorioActual.FullName, "Dockerfile")))
        {
            directorioActual = directorioActual.Parent;
        }

        Assert.NotNull(directorioActual);
        var dockerfilePath = Path.Combine(directorioActual.FullName, "Dockerfile");
        Assert.True(File.Exists(dockerfilePath), $"No se encontró el Dockerfile en {dockerfilePath}");

        var contenido = File.ReadAllText(dockerfilePath);
        var lineas = contenido
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !l.StartsWith('#'))
            .ToList();

        var indiceRuntime = lineas.FindLastIndex(l =>
            l.StartsWith("FROM mcr.microsoft.com/dotnet/aspnet:8.0", StringComparison.OrdinalIgnoreCase));
        Assert.True(indiceRuntime >= 0, "El Dockerfile debe contener la etapa runtime basada en mcr.microsoft.com/dotnet/aspnet:8.0.");

        var lineasEtapaFinal = lineas.Skip(indiceRuntime + 1).ToList();
        Assert.Contains(lineasEtapaFinal, l => string.Equals(l, "USER $APP_UID", StringComparison.Ordinal));
    }
}
