using ObraSync.Asistente.Api.Controllers;
using ObraSync.Asistente.Api.Dtos;
using ObraSync.Asistente.Application;

namespace ObraSync.Asistente.Tests;

public class ValidadorChatTests
{
    [Fact]
    public void Un_pedido_valido_sin_historial_pasa()
    {
        Assert.Null(ValidadorChat.Validar(new ChatRequestDto("Quiero pedir materiales", null)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public void Un_mensaje_vacio_se_rechaza(string? mensaje)
    {
        Assert.NotNull(ValidadorChat.Validar(new ChatRequestDto(mensaje, null)));
    }

    [Fact]
    public void Un_mensaje_de_mas_de_500_caracteres_se_rechaza_y_uno_de_500_pasa()
    {
        Assert.NotNull(ValidadorChat.Validar(new ChatRequestDto(new string('a', 501), null)));
        Assert.Null(ValidadorChat.Validar(new ChatRequestDto(new string('a', 500), null)));
    }

    [Fact]
    public void Un_historial_de_mas_de_10_mensajes_se_rechaza()
    {
        var once = Enumerable.Range(1, 11).Select(_ => new ChatMensajeDto("usuario", "hola")).ToList();
        var diez = once.Take(10).ToList();

        Assert.NotNull(ValidadorChat.Validar(new ChatRequestDto("hola", once)));
        Assert.Null(ValidadorChat.Validar(new ChatRequestDto("hola", diez)));
    }

    [Theory]
    [InlineData("sistema", "hola")]
    [InlineData(null, "hola")]
    [InlineData("usuario", "")]
    [InlineData("asistente", null)]
    public void Un_mensaje_de_historial_con_rol_invalido_o_vacio_se_rechaza(string? rol, string? texto)
    {
        var historial = new List<ChatMensajeDto> { new(rol, texto) };

        Assert.NotNull(ValidadorChat.Validar(new ChatRequestDto("hola", historial)));
    }

    [Fact]
    public void Un_mensaje_de_historial_demasiado_largo_se_rechaza()
    {
        var historial = new List<ChatMensajeDto> { new("usuario", new string('a', Agente.MaxCaracteresHistorial + 1)) };

        Assert.NotNull(ValidadorChat.Validar(new ChatRequestDto("hola", historial)));
    }
}
