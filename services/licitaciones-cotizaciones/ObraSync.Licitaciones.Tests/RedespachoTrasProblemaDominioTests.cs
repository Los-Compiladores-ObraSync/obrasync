using ObraSync.Licitaciones.Domain.Entities;
using Xunit;

namespace ObraSync.Licitaciones.Tests;

/// <summary>
/// Una entrega marcada con problema vuelve al proveedor: puede (y tiene que) despachar de nuevo,
/// y ese redespacho cierra el problema.
/// </summary>
public class RedespachoTrasProblemaDominioTests
{
    private static OrdenCompra OrdenDespachada()
    {
        var proveedorId = Guid.NewGuid();
        var orden = new OrdenCompra
        {
            OrdenCompraId = Guid.NewGuid(),
            ProveedorId = proveedorId,
            Estado = "Emitida",
            FechaEmision = DateTime.UtcNow.AddDays(-1)
        };
        orden.Despachar(proveedorId, DateTime.UtcNow.AddHours(-3));
        return orden;
    }

    [Fact]
    public void SinProblemaNiRemanente_NoPuedeRedespachar_YDespacharDeNuevoFalla()
    {
        var orden = OrdenDespachada();

        Assert.False(orden.PuedeRedespachar);
        Assert.Throws<InvalidOperationException>(() => orden.Despachar(orden.ProveedorId));
    }

    [Fact]
    public void ConProblemaAbierto_ElProveedorPuedeRedespachar()
    {
        var orden = OrdenDespachada();

        orden.AbrirProblema("No llegó nada");

        Assert.True(orden.TieneProblemaAbierto);
        Assert.True(orden.PuedeRedespachar);
    }

    [Fact]
    public void Redespachar_CierraElProblema_YSumaUnNumeroDeDespacho()
    {
        var orden = OrdenDespachada();
        orden.AbrirProblema("Llegó incompleta");

        orden.Despachar(orden.ProveedorId, DateTime.UtcNow);

        Assert.False(orden.TieneProblemaAbierto);
        Assert.NotNull(orden.ProblemaResueltoEn);
        Assert.Equal(orden.ProveedorId, orden.ProblemaResueltoPor);
        Assert.Equal(2, orden.NumeroDespacho);
        Assert.False(orden.PuedeRedespachar);
    }

    [Fact]
    public void UnaOrdenYaRecibidaCompleta_NoSePuedeRedespacharAunqueTengaProblema()
    {
        var orden = OrdenDespachada();
        orden.AbrirProblema("Daño en una bolsa");
        orden.MarcarComoRecibidaCompleta();

        Assert.False(orden.PuedeRedespachar);
        Assert.Throws<InvalidOperationException>(() => orden.Despachar(orden.ProveedorId));
    }
}
