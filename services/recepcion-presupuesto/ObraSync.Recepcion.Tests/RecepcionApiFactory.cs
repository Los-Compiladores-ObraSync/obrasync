using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ObraSync.Recepcion.Tests;

public class RecepcionApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:Secret", "clave-de-pruebas-de-al-menos-32-caracteres");
        builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=localhost,1433;Database=DB_Test;User Id=sa;Password=no-usada;TrustServerCertificate=True;");
    }
}
