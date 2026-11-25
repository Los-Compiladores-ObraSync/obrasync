using ObraSync.Recepcion.Domain.Entities;
using Xunit;

namespace ObraSync.Recepcion.Tests;

public class EsperaNuevoDespachoTests
{
    private static OrdenCompra Orden(DateTime fechaDespacho) => OrdenCompra.Crear(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1_000m,
        new[] { OrdenCompraItem.Crear(Guid.NewGuid(), 10m, "BOLSA", 100m, "Cemento") },
        fechaDespacho: fechaDespacho);

    [Fact]
    public void ElDespachoEsAnteriorAlProblema_EsperaNuevoDespacho()
    {
        var orden = Orden(DateTime.UtcNow.AddHours(-2));

        Assert.True(orden.EsperaNuevoDespachoTras(DateTime.UtcNow));
    }

    [Fact]
    public void ElProveedorDespachaDeNuevoDespuesDelProblema_YaNoEspera()
    {
        var orden = Orden(DateTime.UtcNow.AddHours(-2));
        var problema = DateTime.UtcNow;

        orden.MarcarComoDespachada(problema.AddMinutes(5), esRedespacho: true);

        Assert.False(orden.EsperaNuevoDespachoTras(problema));
    }

    [Fact]
    public void UnEventoRepetidoSinRedespachoConservaElPrimerDespacho()
    {
        var primero = DateTime.UtcNow.AddHours(-2);
        var orden = Orden(primero);

        orden.MarcarComoDespachada(DateTime.UtcNow.AddMinutes(5), esRedespacho: false);

        Assert.Equal(primero, orden.FechaDespacho);
        Assert.True(orden.EsperaNuevoDespachoTras(DateTime.UtcNow));
    }

    [Fact]
    public void UnRedespachoMasViejoQueElActual_NoLoPisa()
    {
        var actual = DateTime.UtcNow.AddHours(-1);
        var orden = Orden(actual);

        orden.MarcarComoDespachada(DateTime.UtcNow.AddHours(-5), esRedespacho: true);

        Assert.Equal(actual, orden.FechaDespacho);
    }
}
