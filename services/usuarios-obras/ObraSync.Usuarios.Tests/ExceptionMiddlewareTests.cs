using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using ObraSync.Usuarios.Api.Middlewares;
using ObraSync.Usuarios.Domain.Exceptions;
using Xunit;

namespace ObraSync.Usuarios.Tests;

/// <summary>Punto 9 de #136: el middleware solo muestra mensajes de excepciones de negocio.</summary>
public class ExceptionMiddlewareTests
{
    private static async Task<(int status, string cuerpo)> EjecutarAsync(Exception aLanzar)
    {
        var contexto = new DefaultHttpContext();
        contexto.Response.Body = new MemoryStream();
        contexto.Request.Path = "/api/prueba";

        var middleware = new ExceptionMiddleware(
            _ => throw aLanzar,
            NullLogger<ExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(contexto);

        contexto.Response.Body.Position = 0;
        var cuerpo = await new StreamReader(contexto.Response.Body).ReadToEndAsync();
        return (contexto.Response.StatusCode, cuerpo);
    }

    [Fact]
    public async Task ValidacionException_Devuelve400ConSuMensaje()
    {
        var (status, cuerpo) = await EjecutarAsync(new ValidacionException("El rol 'X' no es válido."));

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Contains("El rol 'X' no es válido.", cuerpo);
    }

    [Fact]
    public async Task ConflictoException_Devuelve409ConSuMensaje()
    {
        var (status, cuerpo) = await EjecutarAsync(new ConflictoException("Ya existe un usuario registrado."));

        Assert.Equal(StatusCodes.Status409Conflict, status);
        Assert.Contains("Ya existe un usuario registrado.", cuerpo);
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(ArgumentException))]
    public async Task ExcepcionDelFrameworkSinTipoPropio_Devuelve500GenericoSinFiltrarElMensaje(Type tipo)
    {
        var excepcion = (Exception)Activator.CreateInstance(tipo, "cadena de conexion Server=interno;Password=secreto")!;

        var (status, cuerpo) = await EjecutarAsync(excepcion);

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.DoesNotContain("secreto", cuerpo);
        using var json = JsonDocument.Parse(cuerpo);
        Assert.True(json.RootElement.TryGetProperty("correlationId", out _));
    }
}
