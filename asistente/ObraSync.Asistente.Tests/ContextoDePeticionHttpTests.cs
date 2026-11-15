using Microsoft.AspNetCore.Http;
using ObraSync.Asistente.Api.Servicios;

namespace ObraSync.Asistente.Tests;

public class ContextoDePeticionHttpTests
{
    private static ContextoDePeticionHttp Armar(Action<HttpContext> configurar)
    {
        var contexto = new DefaultHttpContext();
        configurar(contexto);
        return new ContextoDePeticionHttp(new HttpContextAccessor { HttpContext = contexto });
    }

    [Fact]
    public void Saca_el_JWT_sin_el_prefijo_Bearer()
    {
        var ctx = Armar(c => c.Request.Headers.Authorization = "Bearer abc.def.ghi");

        Assert.Equal("abc.def.ghi", ctx.Jwt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Basic usuario:clave")]
    [InlineData("Bearer ")]
    public void Sin_un_Bearer_valido_el_JWT_es_null(string cabecera)
    {
        var ctx = Armar(c => c.Request.Headers.Authorization = cabecera);

        Assert.Null(ctx.Jwt);
    }

    [Fact]
    public void Usa_el_X_Correlation_ID_del_pedido()
    {
        var ctx = Armar(c => c.Request.Headers["X-Correlation-ID"] = "corr-1");

        Assert.Equal("corr-1", ctx.CorrelationId);
    }

    [Fact]
    public void Sin_X_Correlation_ID_usa_el_identificador_del_pedido()
    {
        var ctx = Armar(c => c.TraceIdentifier = "trace-9");

        Assert.Equal("trace-9", ctx.CorrelationId);
    }
}
