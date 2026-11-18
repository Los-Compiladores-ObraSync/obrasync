using System;
using System.Collections.Generic;
using ObraSync.Recepcion.Domain.Entities;
using Xunit;

namespace ObraSync.Recepcion.Tests.Domain;

public class OrdenCompraRemanentesTests
{
    [Fact]
    public void CalcularSaldosPendientes_CasoBase_100Pedidas75Recibidas_DevuelveSaldo25()
    {
        // Arrange
        var articuloId = Guid.NewGuid();
        var item = OrdenCompraItem.Crear(articuloId, 100m, "UNIDAD", 500m, "Barras de hierro");
        var oc = OrdenCompra.Crear(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            50000m,
            new List<OrdenCompraItem> { item });

        var recibidosPrevios = new Dictionary<Guid, decimal>
        {
            [articuloId] = 75m
        };

        // Act
        var saldos = oc.CalcularSaldosPendientes(recibidosPrevios);
        decimal saldoIndividual = oc.ObtenerSaldoPendienteArticulo(articuloId, recibidosPrevios);

        // Assert
        Assert.Single(saldos);
        Assert.Equal(25m, saldos[articuloId]);
        Assert.Equal(25m, saldoIndividual);
    }

    [Fact]
    public void CalcularSaldosPendientes_SinRecibidosPrevios_DevuelveCantidadTotalComoSaldo()
    {
        // Arrange
        var articuloId = Guid.NewGuid();
        var item = OrdenCompraItem.Crear(articuloId, 100m, "UNIDAD", 500m, "Barras de hierro");
        var oc = OrdenCompra.Crear(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            50000m,
            new List<OrdenCompraItem> { item });

        // Act & Assert
        var saldosNull = oc.CalcularSaldosPendientes(null);
        var saldosVacio = oc.CalcularSaldosPendientes(new Dictionary<Guid, decimal>());

        Assert.Equal(100m, saldosNull[articuloId]);
        Assert.Equal(100m, saldosVacio[articuloId]);
        Assert.Equal(100m, oc.ObtenerSaldoPendienteArticulo(articuloId));
    }

    [Fact]
    public void ObtenerSaldoPendienteArticulo_ArticuloNoPerteneceAOC_LanzaArgumentException()
    {
        // Arrange
        var item = OrdenCompraItem.Crear(Guid.NewGuid(), 100m, "UNIDAD", 500m, "Barras");
        var oc = OrdenCompra.Crear(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            50000m,
            new List<OrdenCompraItem> { item });

        // Act & Assert
        Assert.Throws<ArgumentException>(() => oc.ObtenerSaldoPendienteArticulo(Guid.NewGuid()));
    }

    [Fact]
    public void EncadenarEntregas_CuatroEntregasSucesivas_CalculaSaldosEncadenadosCorrectamente()
    {
        // Arrange: OC de 100 barras
        var articuloId = Guid.NewGuid();
        var item = OrdenCompraItem.Crear(articuloId, 100m, "UNIDAD", 500m, "Barras de hierro");
        var oc = OrdenCompra.Crear(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            50000m,
            new List<OrdenCompraItem> { item });

        // Secuencia: 40 -> 35 -> 20 -> 5
        var entregas = new List<IReadOnlyDictionary<Guid, decimal>>
        {
            new Dictionary<Guid, decimal> { [articuloId] = 40m },
            new Dictionary<Guid, decimal> { [articuloId] = 35m },
            new Dictionary<Guid, decimal> { [articuloId] = 20m },
            new Dictionary<Guid, decimal> { [articuloId] = 5m }
        };

        // Act
        var historial = oc.EncadenarEntregas(entregas);

        // Assert
        Assert.Equal(4, historial.Count);
        Assert.Equal(60m, historial[0][articuloId]); // 100 - 40 = 60
        Assert.Equal(25m, historial[1][articuloId]); // 60 - 35 = 25
        Assert.Equal(5m, historial[2][articuloId]);  // 25 - 20 = 5
        Assert.Equal(0m, historial[3][articuloId]);  // 5 - 5 = 0 (completa)
    }

    [Fact]
    public void EncadenarEntregas_EntregaIntermediaSuperaSaldoVigente_LanzaInvalidOperationException()
    {
        // Arrange: OC de 100 barras
        var articuloId = Guid.NewGuid();
        var item = OrdenCompraItem.Crear(articuloId, 100m, "UNIDAD", 500m, "Barras");
        var oc = OrdenCompra.Crear(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            50000m,
            new List<OrdenCompraItem> { item });

        // Secuencia: primera entrega de 40 (saldo 60), segunda de 65 (supera saldo vigente de 60)
        var entregas = new List<IReadOnlyDictionary<Guid, decimal>>
        {
            new Dictionary<Guid, decimal> { [articuloId] = 40m },
            new Dictionary<Guid, decimal> { [articuloId] = 65m }
        };

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => oc.EncadenarEntregas(entregas));
        Assert.Contains("supera el saldo remanente vigente", ex.Message);
    }

    [Fact]
    public void EncadenarEntregas_CantidadRecibidaNegativa_LanzaArgumentException()
    {
        // Arrange
        var articuloId = Guid.NewGuid();
        var item = OrdenCompraItem.Crear(articuloId, 100m, "UNIDAD", 500m, "Barras");
        var oc = OrdenCompra.Crear(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            50000m,
            new List<OrdenCompraItem> { item });

        var entregas = new List<IReadOnlyDictionary<Guid, decimal>>
        {
            new Dictionary<Guid, decimal> { [articuloId] = -10m }
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => oc.EncadenarEntregas(entregas));
    }

    [Fact]
    public void MultiItem_EntregasParcialesAsimetricas_CalculaSaldosCorrectamente()
    {
        // Arrange: OC con 3 artículos
        var art1 = Guid.NewGuid(); // 100 barras
        var art2 = Guid.NewGuid(); // 50 bolsas cemento
        var art3 = Guid.NewGuid(); // 20 chapas

        var items = new List<OrdenCompraItem>
        {
            OrdenCompraItem.Crear(art1, 100m, "UNIDAD", 100m, "Barras"),
            OrdenCompraItem.Crear(art2, 50m, "BOLSA", 200m, "Cemento"),
            OrdenCompraItem.Crear(art3, 20m, "UNIDAD", 300m, "Chapas")
        };

        var oc = OrdenCompra.Crear(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            26000m,
            items);

        // Act: 3 entregas asimétricas
        var entregas = new List<IReadOnlyDictionary<Guid, decimal>>
        {
            // Entrega 1: art1=50, art2=50 (se completa cemento), art3=0
            new Dictionary<Guid, decimal> { [art1] = 50m, [art2] = 50m, [art3] = 0m },
            // Entrega 2: art1=30, art2=0, art3=15
            new Dictionary<Guid, decimal> { [art1] = 30m, [art2] = 0m, [art3] = 15m },
            // Entrega 3: saldo final art1=20, art2=0, art3=5
            new Dictionary<Guid, decimal> { [art1] = 20m, [art2] = 0m, [art3] = 5m }
        };

        var historial = oc.EncadenarEntregas(entregas);

        // Assert
        Assert.Equal(3, historial.Count);

        // Paso 1
        Assert.Equal(50m, historial[0][art1]);
        Assert.Equal(0m, historial[0][art2]);
        Assert.Equal(20m, historial[0][art3]);

        // Paso 2
        Assert.Equal(20m, historial[1][art1]);
        Assert.Equal(0m, historial[1][art2]);
        Assert.Equal(5m, historial[1][art3]);

        // Paso 3 (todo completado)
        Assert.Equal(0m, historial[2][art1]);
        Assert.Equal(0m, historial[2][art2]);
        Assert.Equal(0m, historial[2][art3]);
    }
}
