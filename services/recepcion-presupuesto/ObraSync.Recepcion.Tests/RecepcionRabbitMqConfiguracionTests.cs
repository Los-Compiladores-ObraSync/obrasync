using System;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ObraSync.Recepcion.Tests;

public class RecepcionRabbitMqConfiguracionTests
{
    [Fact]
    public void Arrancar_SinCredencialesRabbitMq_LanzaInvalidOperationException()
    {
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Jwt:Secret", RecepcionApiFactory.TestJwtSecret);
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=localhost,1433;Database=DB_Test;User Id=sa;Password=no-usada;TrustServerCertificate=True;");
                builder.UseSetting("RabbitMQ:UseInMemory", "false");
                builder.UseSetting("RabbitMQ:Username", "");
                builder.UseSetting("RabbitMQ:Password", "");
            });

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("RabbitMQ Username y Password deben estar configurados", ex.ToString());
    }
}
