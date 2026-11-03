using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ObraSync.Recepcion.Api.Controllers;
using ObraSync.Recepcion.Api.DTOs;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Domain.Enums;
using ObraSync.Recepcion.Infrastructure.Persistence;
using RecepcionEntity = ObraSync.Recepcion.Domain.Entities.Recepcion;

namespace ObraSync.Recepcion.Tests;

public class ObtenerOrdenesPendientesTests
{
    private static RecepcionDbContext CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<RecepcionDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new RecepcionDbContext(options);
    }

    private static RecepcionesController CrearController(RecepcionDbContext db) =>
        new(db,
            NullLogger<RecepcionesController>.Instance,
            Substitute.For<IRecepcionEventPublisher>());

    private static OrdenCompra CrearOrden(Guid id, Guid obraId, Guid articuloId, decimal cantidad = 10) =>
        OrdenCompra.Crear(
            id,
            obraId,
            Guid.NewGuid(),
            1000,
            new List<OrdenCompraItem>
            {
                OrdenCompraItem.Crear(articuloId, cantidad, "UN", 10, "Cemento")
            });

    private static List<OrdenPendienteDto> ExtraerLista(IActionResult resultado)
    {
        var ok = Assert.IsType<OkObjectResult>(resultado);
        return Assert.IsAssignableFrom<List<OrdenPendienteDto>>(ok.Value);
    }

    [Fact]
    public async Task OrdenesPendientes_SinRecepciones_DevuelveLaOC()
    {
        // Arrange
        using var db = CreateInMemoryContext($"Db_Pend_SinRec_{Guid.NewGuid()}");
        var ocId = Guid.NewGuid();
        var obraId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        db.OrdenesCompra.Add(CrearOrden(ocId, obraId, articuloId));
        await db.SaveChangesAsync();
        var controller = CrearController(db);

        // Act
        var resultado = await controller.ObtenerOrdenesPendientes(null, CancellationToken.None);

        // Assert
        var orden = Assert.Single(ExtraerLista(resultado));
        Assert.Equal(ocId, orden.OrdenCompraId);
        Assert.Equal(obraId, orden.ObraId);
        var item = Assert.Single(orden.Items);
        Assert.Equal(articuloId, item.ArticuloId);
        Assert.Equal("Cemento", item.DescripcionArticulo);
        Assert.Equal("UN", item.UnidadMedida);
        Assert.Equal(10, item.CantidadEsperada);
        Assert.Equal(0, item.CantidadRecibida);
    }

    [Fact]
    public async Task OrdenesPendientes_ConRecepcionCompleta_NoLaDevuelve()
    {
        // Arrange: una OC con recepción completa y otra sin recepciones
        using var db = CreateInMemoryContext($"Db_Pend_Completa_{Guid.NewGuid()}");
        var ocCompleta = Guid.NewGuid();
        var ocPendiente = Guid.NewGuid();
        var obraId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        db.OrdenesCompra.AddRange(
            CrearOrden(ocCompleta, obraId, articuloId),
            CrearOrden(ocPendiente, obraId, articuloId));
        db.Recepciones.Add(new RecepcionEntity
        {
            Id = Guid.NewGuid(),
            ObraId = obraId,
            OrdenCompraId = ocCompleta,
            NumeroRemito = "R-COMPLETA",
            Estado = EstadoRecepcion.Completa
        });
        await db.SaveChangesAsync();
        var controller = CrearController(db);

        // Act
        var resultado = await controller.ObtenerOrdenesPendientes(null, CancellationToken.None);

        // Assert
        var lista = ExtraerLista(resultado);
        var unica = Assert.Single(lista);
        Assert.Equal(ocPendiente, unica.OrdenCompraId);
        Assert.DoesNotContain(lista, o => o.OrdenCompraId == ocCompleta);
    }

    [Fact]
    public async Task OrdenesPendientes_FiltroObra_DevuelveSoloEsaObra()
    {
        // Arrange
        using var db = CreateInMemoryContext($"Db_Pend_Obra_{Guid.NewGuid()}");
        var obraBuscada = Guid.NewGuid();
        var obraOtra = Guid.NewGuid();
        var ocBuscada = Guid.NewGuid();
        var ocOtra = Guid.NewGuid();
        db.OrdenesCompra.AddRange(
            CrearOrden(ocBuscada, obraBuscada, Guid.NewGuid()),
            CrearOrden(ocOtra, obraOtra, Guid.NewGuid()));
        await db.SaveChangesAsync();
        var controller = CrearController(db);

        // Act
        var resultado = await controller.ObtenerOrdenesPendientes(obraBuscada, CancellationToken.None);

        // Assert
        var lista = ExtraerLista(resultado);
        var unica = Assert.Single(lista);
        Assert.Equal(ocBuscada, unica.OrdenCompraId);
        Assert.Equal(obraBuscada, unica.ObraId);
        Assert.DoesNotContain(lista, o => o.OrdenCompraId == ocOtra);
    }

    [Fact]
    public async Task OrdenesPendientes_SinOC_DevuelveListaVacia()
    {
        // Arrange
        using var db = CreateInMemoryContext($"Db_Pend_Vacia_{Guid.NewGuid()}");
        var controller = CrearController(db);

        // Act
        var resultado = await controller.ObtenerOrdenesPendientes(null, CancellationToken.None);

        // Assert
        Assert.Empty(ExtraerLista(resultado));
    }
}

