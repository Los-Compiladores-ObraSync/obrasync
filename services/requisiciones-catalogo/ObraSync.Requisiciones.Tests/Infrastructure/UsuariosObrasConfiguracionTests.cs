using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ObraSync.Requisiciones.Application.Interfaces;
using ObraSync.Requisiciones.Infrastructure;
using ObraSync.Requisiciones.Infrastructure.Services;
using Xunit;

namespace ObraSync.Requisiciones.Tests.Infrastructure;

public class UsuariosObrasConfiguracionTests
{
    private static ServiceCollection Registrar(string useInMemory, string? baseUrl)
    {
        var configDict = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=test;Trusted_Connection=True;TrustServerCertificate=True;",
            ["RabbitMQ:UseInMemory"] = useInMemory,
            ["RabbitMQ:Username"] = "usuario-de-prueba",
            ["RabbitMQ:Password"] = "clave-de-prueba"
        };

        if (baseUrl != null) configDict["UsuariosObras:BaseUrl"] = baseUrl;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        var services = new ServiceCollection();
        services.AddInfrastructureServices(configuration);
        return services;
    }

    private static Type ImplementacionDelValidador(ServiceCollection services)
        => services.Single(d => d.ServiceType == typeof(IObraAsignacionValidator)).ImplementationType!;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SinBaseUrlFueraDePruebasAisladas_LanzaInvalidOperationException(string? baseUrl)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Registrar("false", baseUrl));

        Assert.Contains("UsuariosObras:BaseUrl", ex.Message);
    }

    [Fact]
    public void SinBaseUrlEnPruebasAisladas_UsaElValidadorDeshabilitado()
    {
        var services = Registrar("true", null);

        Assert.Equal(typeof(ValidadorObraAsignacionDeshabilitado), ImplementacionDelValidador(services));
    }

    [Fact]
    public void ConBaseUrl_UsaElValidadorHttp()
    {
        var services = Registrar("false", "http://usuarios-obras-api:8080");

        Assert.Equal(typeof(HttpObraAsignacionValidator), ImplementacionDelValidador(services));
    }
}
