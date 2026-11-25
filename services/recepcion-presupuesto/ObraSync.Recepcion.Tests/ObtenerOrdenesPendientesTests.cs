using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ObraSync.Recepcion.Api.Controllers;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Handlers;
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

    private static OrdenesRecepcionController CrearController(RecepcionDbContext db) =>
        new(
            new ObtenerOrdenesPendientesQueryHandler(db),
            new ObtenerOrdenesBloqueadasQueryHandler(db),
            new LiberarOrdenCompraCommandHandler(db),
            new LiquidarOrdenCompraCommandHandler(db));

    private static OrdenCompra CrearOrden(Guid id, Guid obraId, Guid articuloId, decimal cantidad = 10, DateTime? fechaDespacho = null) =>
        OrdenCompra.Crear(
            id,
            obraId,
            Guid.NewGuid(),
            1000,
            new List<OrdenCompraItem>
            {
                OrdenCompraItem.Crear(articuloId, cantidad, "UN", 10, "Cemento")
            },
            fechaDespacho: fechaDespacho ?? DateTime.UtcNow);

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

    [Fact]
    public async Task OrdenesPendientes_ConOCBloqueada_NoLaDevuelve()
    {
        // Arrange (#95, #154): una OC bloqueada por desvío no debe aparecer en órdenes pendientes
        using var db = CreateInMemoryContext($"Db_Pend_Bloq_{Guid.NewGuid()}");
        var ocActivaId = Guid.NewGuid();
        var ocBloqueadaId = Guid.NewGuid();
        var obraId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();

        var ocActiva = CrearOrden(ocActivaId, obraId, articuloId);
        var ocBloqueada = CrearOrden(ocBloqueadaId, obraId, articuloId);
        ocBloqueada.BloquearPorDesvioPresupuestario("Supera presupuesto de etapa");

        db.OrdenesCompra.AddRange(ocActiva, ocBloqueada);
        await db.SaveChangesAsync();

        var controller = CrearController(db);

        // Act
        var resultado = await controller.ObtenerOrdenesPendientes(null, CancellationToken.None);

        // Assert
        var lista = ExtraerLista(resultado);
        var unica = Assert.Single(lista);
        Assert.Equal(ocActivaId, unica.OrdenCompraId);
        Assert.DoesNotContain(lista, o => o.OrdenCompraId == ocBloqueadaId);
    }

    [Fact]
    public async Task OrdenesPendientes_FiltroSinPartidaTrue_DevuelveSoloOrdenesSinPartida()
    {
        // Arrange (#196)
        using var db = CreateInMemoryContext($"Db_Pend_SinPartida_{Guid.NewGuid()}");
        var obraId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();

        var ocConPartidaId = Guid.NewGuid();
        var ocSinPartidaId = Guid.NewGuid();

        var ocConPartida = CrearOrden(ocConPartidaId, obraId, articuloId);
        var ocSinPartida = CrearOrden(ocSinPartidaId, obraId, articuloId);
        ocSinPartida.MarcarSinPartida();

        db.OrdenesCompra.AddRange(ocConPartida, ocSinPartida);
        await db.SaveChangesAsync();

        var controller = CrearController(db);

        // Act
        var resultado = await controller.ObtenerOrdenesPendientes(obraId, sinPartida: true, CancellationToken.None);

        // Assert
        var lista = ExtraerLista(resultado);
        var unica = Assert.Single(lista);
        Assert.Equal(ocSinPartidaId, unica.OrdenCompraId);
        Assert.True(unica.SinPartidaPresupuestaria);
    }

    [Fact]
    public async Task OrdenesPendientes_FiltroSinPartidaFalse_DevuelveSoloOrdenesConPartida()
    {
        // Arrange (#196)
        using var db = CreateInMemoryContext($"Db_Pend_ConPartida_{Guid.NewGuid()}");
        var obraId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();

        var ocConPartidaId = Guid.NewGuid();
        var ocSinPartidaId = Guid.NewGuid();

        var ocConPartida = CrearOrden(ocConPartidaId, obraId, articuloId);
        var ocSinPartida = CrearOrden(ocSinPartidaId, obraId, articuloId);
        ocSinPartida.MarcarSinPartida();

        db.OrdenesCompra.AddRange(ocConPartida, ocSinPartida);
        await db.SaveChangesAsync();

        var controller = CrearController(db);

        // Act
        var resultado = await controller.ObtenerOrdenesPendientes(obraId, sinPartida: false, CancellationToken.None);

        // Assert
        var lista = ExtraerLista(resultado);
        var unica = Assert.Single(lista);
        Assert.Equal(ocConPartidaId, unica.OrdenCompraId);
        Assert.False(unica.SinPartidaPresupuestaria);
    }

    [Fact]
    public async Task OrdenesPendientes_MapeaEtapaYSinPartidaPresupuestaria()
    {
        // Arrange (#196)
        using var db = CreateInMemoryContext($"Db_Pend_Mapeo_{Guid.NewGuid()}");
        var obraId = Guid.NewGuid();
        var ocId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();

        var oc = OrdenCompra.Crear(
            ocId, obraId, Guid.NewGuid(), 5000m,
            new[] { OrdenCompraItem.Crear(articuloId, 5m, "UN", 1000m, "Hierro") },
            etapa: "Estructura",
            sinPartidaPresupuestaria: true,
            fechaDespacho: DateTime.UtcNow);

        db.OrdenesCompra.Add(oc);
        await db.SaveChangesAsync();

        var controller = CrearController(db);

        // Act
        var resultado = await controller.ObtenerOrdenesPendientes(null, CancellationToken.None);

        // Assert
        var lista = ExtraerLista(resultado);
        var orden = Assert.Single(lista);
        Assert.Equal(ocId, orden.OrdenCompraId);
        Assert.Equal("Estructura", orden.Etapa);
        Assert.True(orden.SinPartidaPresupuestaria);
    }

    [Fact]
    public async Task OrdenesPendientes_OrdenActivaSinDespachar_NoSeDevuelve()
    {
        using var db = CreateInMemoryContext($"Db_Pend_NoDesp_{Guid.NewGuid()}");
        var obraId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();

        var ocSinDespachar = OrdenCompra.Crear(
            Guid.NewGuid(),
            obraId,
            Guid.NewGuid(),
            1000m,
            new[] { OrdenCompraItem.Crear(articuloId, 10m, "UN", 100m, "Cemento") }); // FechaDespacho = null

        var ocDespachada = CrearOrden(Guid.NewGuid(), obraId, articuloId);

        db.OrdenesCompra.AddRange(ocSinDespachar, ocDespachada);
        await db.SaveChangesAsync();

        var controller = CrearController(db);
        var resultado = await controller.ObtenerOrdenesPendientes(obraId, CancellationToken.None);

        var lista = ExtraerLista(resultado);
        var unica = Assert.Single(lista);
        Assert.Equal(ocDespachada.Id, unica.OrdenCompraId);
    }
}

