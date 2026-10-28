
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Domain.Enums;
using ObraSync.Recepcion.Domain.Exceptions;
using RecepcionEntity = ObraSync.Recepcion.Domain.Entities.Recepcion;

namespace ObraSync.Recepcion.Tests;

public class RecepcionEstadosTests
{
    [Theory]
    [InlineData(EstadoRecepcion.Pendiente, EstadoRecepcion.Parcial)]
    [InlineData(EstadoRecepcion.Pendiente, EstadoRecepcion.Completa)]
    [InlineData(EstadoRecepcion.Pendiente, EstadoRecepcion.Rechazada)]
    [InlineData(EstadoRecepcion.Parcial, EstadoRecepcion.Parcial)]
    [InlineData(EstadoRecepcion.Parcial, EstadoRecepcion.Completa)]
    public void CambiarEstado_TransicionPermitida_ActualizaEstado(
        EstadoRecepcion estadoInicial,
        EstadoRecepcion estadoNuevo)
    {
        var recepcion = new RecepcionEntity { Estado = estadoInicial };

        recepcion.CambiarEstado(estadoNuevo);

        Assert.Equal(estadoNuevo, recepcion.Estado);
        Assert.NotNull(recepcion.UpdatedAt);
    }

    [Theory]
    [InlineData(EstadoRecepcion.Pendiente, EstadoRecepcion.Pendiente)]
    [InlineData(EstadoRecepcion.Parcial, EstadoRecepcion.Pendiente)]
    [InlineData(EstadoRecepcion.Parcial, EstadoRecepcion.Rechazada)]
    [InlineData(EstadoRecepcion.Completa, EstadoRecepcion.Pendiente)]
    [InlineData(EstadoRecepcion.Completa, EstadoRecepcion.Parcial)]
    [InlineData(EstadoRecepcion.Completa, EstadoRecepcion.Rechazada)]
    [InlineData(EstadoRecepcion.Completa, EstadoRecepcion.Completa)]
    [InlineData(EstadoRecepcion.Rechazada, EstadoRecepcion.Pendiente)]
    [InlineData(EstadoRecepcion.Rechazada, EstadoRecepcion.Parcial)]
    [InlineData(EstadoRecepcion.Rechazada, EstadoRecepcion.Completa)]
    [InlineData(EstadoRecepcion.Rechazada, EstadoRecepcion.Rechazada)]
    public void CambiarEstado_TransicionProhibida_LanzaExcepcion(
        EstadoRecepcion estadoInicial,
        EstadoRecepcion estadoNuevo)
    {
        var recepcion = new RecepcionEntity { Estado = estadoInicial };

        Assert.Throws<TransicionEstadoInvalidaException>(
            () => recepcion.CambiarEstado(estadoNuevo));

        Assert.Equal(estadoInicial, recepcion.Estado);
        Assert.Null(recepcion.UpdatedAt);
    }

    [Fact]
    public void CalcularEstado_EntregaCompleta_DevuelveCompleta()
    {
        var recepcion = CrearRecepcion(100, 100);

        var estado = recepcion.CalcularEstadoSegunCantidades();

        Assert.Equal(EstadoRecepcion.Completa, estado);
    }

    [Fact]
    public void CalcularEstado_EntregaParcial_DevuelveParcial()
    {
        // Caso oficial CU-06: se esperaban 100 barras y llegaron 75.
        var recepcion = CrearRecepcion(100, 75);

        var estado = recepcion.CalcularEstadoSegunCantidades();

        Assert.Equal(EstadoRecepcion.Parcial, estado);

        var detalle = recepcion.Detalles.First();
        Assert.Equal(25m, detalle.CantidadEsperada - detalle.CantidadRecibida);
    }

    [Fact]
    public void CalcularEstado_VariosArticulosConUnoIncompleto_DevuelveParcial()
    {
        var recepcion = CrearRecepcion(100, 100);

        recepcion.Detalles.Add(new DetalleRecepcion
        {
            CantidadEsperada = 50,
            CantidadRecibida = 30
        });

        var estado = recepcion.CalcularEstadoSegunCantidades();

        Assert.Equal(EstadoRecepcion.Parcial, estado);
    }

    [Fact]
    public void CalcularEstado_SinDetalles_LanzaExcepcion()
    {
        var recepcion = new RecepcionEntity();

        Assert.Throws<InvalidOperationException>(
            () => recepcion.CalcularEstadoSegunCantidades());
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-10, 0)]
    [InlineData(100, -1)]
    [InlineData(100, 101)]
    public void CalcularEstado_CantidadesInvalidas_LanzaExcepcion(
        int esperada,
        int recibida)
    {
        var recepcion = CrearRecepcion(esperada, recibida);

        Assert.Throws<InvalidOperationException>(
            () => recepcion.CalcularEstadoSegunCantidades());
    }

    private static RecepcionEntity CrearRecepcion(
        decimal cantidadEsperada,
        decimal cantidadRecibida)
    {
        var recepcion = new RecepcionEntity();

        recepcion.Detalles.Add(new DetalleRecepcion
        {
            CantidadEsperada = cantidadEsperada,
            CantidadRecibida = cantidadRecibida
        });

        return recepcion;
    }
}
