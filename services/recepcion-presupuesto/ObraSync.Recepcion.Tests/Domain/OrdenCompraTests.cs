using System.Text.Json;
using ObraSync.Recepcion.Domain.Entities;
using Xunit;

namespace ObraSync.Recepcion.Tests.Domain;

public class OrdenCompraTests
{
    [Fact]
    public void Crear_ConDatosValidos_DebeCrearOrdenCompraYAsignarItemsCorrectamente()
    {
        // Arrange
        var ordenCompraId = Guid.NewGuid();
        var obraId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();
        var articuloId1 = Guid.NewGuid();
        var articuloId2 = Guid.NewGuid();

        var item1 = OrdenCompraItem.Crear(articuloId1, 100.5m, "BOLSA", 1500.50m, "Cemento Portland");
        var item2 = OrdenCompraItem.Crear(articuloId2, 50m, "KG", 850m, "Cal hidráulica");

        // Act
        var oc = OrdenCompra.Crear(
            ordenCompraId,
            obraId,
            proveedorId,
            193550.25m,
            new[] { item1, item2 },
            etapa: "Estructura");

        // Assert
        Assert.Equal(ordenCompraId, oc.Id);
        Assert.Equal(obraId, oc.ObraId);
        Assert.Equal(proveedorId, oc.ProveedorId);
        Assert.Equal(193550.25m, oc.MontoTotal);
        Assert.Equal("Estructura", oc.Etapa);
        Assert.Equal(2, oc.Items.Count);
        Assert.All(oc.Items, item => Assert.Equal(ordenCompraId, item.OrdenCompraId));
    }

    [Fact]
    public void Crear_ConIdVacio_DebeLanzarArgumentException()
    {
        var item = OrdenCompraItem.Crear(Guid.NewGuid(), 10m, "UNIDAD", 100m);

        Assert.Throws<ArgumentException>(() =>
            OrdenCompra.Crear(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), 1000m, new[] { item }));
    }

    [Fact]
    public void Crear_ConObraIdVacio_DebeLanzarArgumentException()
    {
        var item = OrdenCompraItem.Crear(Guid.NewGuid(), 10m, "UNIDAD", 100m);

        Assert.Throws<ArgumentException>(() =>
            OrdenCompra.Crear(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), 1000m, new[] { item }));
    }

    [Fact]
    public void Crear_ConProveedorIdVacio_DebeLanzarArgumentException()
    {
        var item = OrdenCompraItem.Crear(Guid.NewGuid(), 10m, "UNIDAD", 100m);

        Assert.Throws<ArgumentException>(() =>
            OrdenCompra.Crear(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, 1000m, new[] { item }));
    }

    [Fact]
    public void Crear_ConMontoNegativo_DebeLanzarArgumentException()
    {
        var item = OrdenCompraItem.Crear(Guid.NewGuid(), 10m, "UNIDAD", 100m);

        Assert.Throws<ArgumentException>(() =>
            OrdenCompra.Crear(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), -0.01m, new[] { item }));
    }

    [Fact]
    public void Crear_SinItems_DebeLanzarArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            OrdenCompra.Crear(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1000m, Array.Empty<OrdenCompraItem>()));
    }

    [Fact]
    public void Crear_ConItemsNulos_DebeLanzarArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            OrdenCompra.Crear(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1000m, null!));
    }

    [Fact]
    public void Crear_ConArticulosDuplicados_DebeLanzarArgumentException()
    {
        // Regla provisoria: se rechazan duplicados por ArticuloId en la misma OC
        var articuloDuplicadoId = Guid.NewGuid();
        var item1 = OrdenCompraItem.Crear(articuloDuplicadoId, 10m, "BOLSA", 1000m);
        var item2 = OrdenCompraItem.Crear(articuloDuplicadoId, 20m, "BOLSA", 1000m);

        var ex = Assert.Throws<ArgumentException>(() =>
            OrdenCompra.Crear(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 30000m, new[] { item1, item2 }));

        Assert.Contains("artículos duplicados", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Crear_ConEtapaMayorA200Caracteres_DebeLanzarArgumentException()
    {
        var item = OrdenCompraItem.Crear(Guid.NewGuid(), 10m, "UNIDAD", 100m);
        var etapaInvalida = new string('A', 201);

        Assert.Throws<ArgumentException>(() =>
            OrdenCompra.Crear(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1000m, new[] { item }, etapa: etapaInvalida));
    }

    [Fact]
    public void CrearItem_ConArticuloIdVacio_DebeLanzarArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            OrdenCompraItem.Crear(Guid.Empty, 10m, "UNIDAD", 100m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-0.001)]
    public void CrearItem_ConCantidadMenorOIgualACero_DebeLanzarArgumentException(decimal cantidadInvalida)
    {
        Assert.Throws<ArgumentException>(() =>
            OrdenCompraItem.Crear(Guid.NewGuid(), cantidadInvalida, "UNIDAD", 100m));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-0.01)]
    public void CrearItem_ConPrecioNegativo_DebeLanzarArgumentException(decimal precioInvalido)
    {
        Assert.Throws<ArgumentException>(() =>
            OrdenCompraItem.Crear(Guid.NewGuid(), 10m, "UNIDAD", precioInvalido));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CrearItem_ConUnidadMedidaVacia_DebeLanzarArgumentException(string? unidadInvalida)
    {
        Assert.Throws<ArgumentException>(() =>
            OrdenCompraItem.Crear(Guid.NewGuid(), 10m, unidadInvalida!, 100m));
    }

    [Fact]
    public void CrearItem_ConUnidadMedidaMayorA50Caracteres_DebeLanzarArgumentException()
    {
        var unidadLarga = new string('X', 51);

        Assert.Throws<ArgumentException>(() =>
            OrdenCompraItem.Crear(Guid.NewGuid(), 10m, unidadLarga, 100m));
    }

    [Fact]
    public void OrdenCompraItem_SerializacionJson_NoDebeIncluirNavegacionOrdenCompra()
    {
        // Arrange
        var ordenCompraId = Guid.NewGuid();
        var item = OrdenCompraItem.Crear(Guid.NewGuid(), 5m, "UNIDAD", 200m, "Item de prueba");

        var oc = OrdenCompra.Crear(
            ordenCompraId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            1000m,
            new[] { item });

        // Simula la asignación de navegación bidireccional
        item.OrdenCompra = oc;

        // Act
        var json = JsonSerializer.Serialize(item);

        // Assert
        Assert.DoesNotContain("\"OrdenCompra\"", json);
        Assert.Contains("\"ArticuloId\"", json);
        Assert.Contains("\"Cantidad\"", json);
        Assert.Contains("\"UnidadMedida\"", json);
    }
}
