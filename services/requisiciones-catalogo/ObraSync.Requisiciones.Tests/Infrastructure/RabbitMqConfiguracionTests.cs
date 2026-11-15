using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ObraSync.Requisiciones.Infrastructure;
using Xunit;

namespace ObraSync.Requisiciones.Tests.Infrastructure;

public class RabbitMqConfiguracionTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("user", "")]
    [InlineData("", "pass")]
    [InlineData("   ", "   ")]
    public void AddInfrastructureServices_SinCredencialesRabbitMq_LanzaInvalidOperationException(string? username, string? password)
    {
        var services = new ServiceCollection();
        var configDict = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=test;Trusted_Connection=True;TrustServerCertificate=True;",
            ["RabbitMQ:UseInMemory"] = "false"
        };

        if (username != null) configDict["RabbitMQ:Username"] = username;
        if (password != null) configDict["RabbitMQ:Password"] = password;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddInfrastructureServices(configuration));
        Assert.Contains("RabbitMQ Username y Password deben estar configurados", ex.Message);
    }
}
