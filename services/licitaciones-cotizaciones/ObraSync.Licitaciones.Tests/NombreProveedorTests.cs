using ObraSync.Licitaciones.Application.Services;
using Xunit;

namespace ObraSync.Licitaciones.Tests;

public class NombreProveedorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Un_nombre_vacio_se_guarda_como_null(string? entrada)
    {
        Assert.Null(NombreProveedor.Limpiar(entrada));
    }

    [Fact]
    public void Recorta_espacios_y_reemplaza_caracteres_de_control()
    {
        Assert.Equal("Ferretería Sur", NombreProveedor.Limpiar("  Ferretería Sur  "));
        Assert.Equal("Ferretería Sur", NombreProveedor.Limpiar("Ferretería\nSur"));
    }

    [Fact]
    public void Corta_a_200_caracteres()
    {
        var resultado = NombreProveedor.Limpiar(new string('a', 500));

        Assert.NotNull(resultado);
        Assert.Equal(NombreProveedor.MaxLargo, resultado!.Length);
    }
}
