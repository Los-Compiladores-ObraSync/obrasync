using ObraSync.Recepcion.Api.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ObraSync.Recepcion.Api.Controllers;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Infrastructure.Persistence;
using RecepcionEntity = ObraSync.Recepcion.Domain.Entities.Recepcion;

namespace ObraSync.Recepcion.Tests;

public class ObtenerRecepcionesFiltrosTests
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

    private static RecepcionEntity CrearRecepcion(Guid id, Guid obraId, Guid ordenCompraId, string remito) =>
        new()
        {
            Id = id,
            ObraId = obraId,
            OrdenCompraId = ordenCompraId,
            NumeroRemito = remito
        };

    private static List<RecepcionDto> ExtraerLista(IActionResult resultado)
    {
        var ok = Assert.IsType<OkObjectResult>(resultado);
        return Assert.IsAssignableFrom<List<RecepcionDto>>(ok.Value);
    }

    [Fact]
    public async Task ObtenerRecepciones_SinFiltros_DevuelveTodas()
    {
        // Arrange
        using var db = CreateInMemoryContext($"Db_Filtros_Sin_{Guid.NewGuid()}");
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        db.Recepciones.AddRange(
            CrearRecepcion(id1, Guid.NewGuid(), Guid.NewGuid(), "R1"),
            CrearRecepcion(id2, Guid.NewGuid(), Guid.NewGuid(), "R2"));
        await db.SaveChangesAsync();
        var controller = CrearController(db);

        // Act
        var resultado = await controller.ObtenerRecepciones(null, CancellationToken.None);

        // Assert
        var lista = ExtraerLista(resultado);
        Assert.Equal(2, lista.Count);
        Assert.Contains(lista, r => r.Id == id1);
        Assert.Contains(lista, r => r.Id == id2);
    }

    [Fact]
    public async Task ObtenerRecepciones_FiltroOrdenCompra_DevuelveSoloLaDeEsaOC()
    {
        // Arrange
        using var db = CreateInMemoryContext($"Db_Filtros_OC_{Guid.NewGuid()}");
        var ocBuscada = Guid.NewGuid();
        var idEsperado = Guid.NewGuid();
        var idOtra = Guid.NewGuid();
        db.Recepciones.AddRange(
            CrearRecepcion(idEsperado, Guid.NewGuid(), ocBuscada, "R-ESPERADA"),
            CrearRecepcion(idOtra, Guid.NewGuid(), Guid.NewGuid(), "R-OTRA"));
        await db.SaveChangesAsync();
        var controller = CrearController(db);

        // Act
        var resultado = await controller.ObtenerRecepciones(
            null, CancellationToken.None, ordenCompraId: ocBuscada);

        // Assert
        var lista = ExtraerLista(resultado);
        var unica = Assert.Single(lista);
        Assert.Equal(idEsperado, unica.Id);
        Assert.Equal(ocBuscada, unica.OrdenCompraId);
        Assert.DoesNotContain(lista, r => r.Id == idOtra);
    }

    [Fact]
    public async Task ObtenerRecepciones_FiltroOrdenCompraInexistente_DevuelveVacio()
    {
        // Arrange
        using var db = CreateInMemoryContext($"Db_Filtros_Inexistente_{Guid.NewGuid()}");
        db.Recepciones.AddRange(
            CrearRecepcion(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "R1"),
            CrearRecepcion(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "R2"));
        await db.SaveChangesAsync();
        var controller = CrearController(db);

        // Act
        var resultado = await controller.ObtenerRecepciones(
            null, CancellationToken.None, ordenCompraId: Guid.NewGuid());

        // Assert
        Assert.Empty(ExtraerLista(resultado));
    }

    [Fact]
    public async Task ObtenerRecepciones_FiltroObraYOrdenCompra_AplicaAmbos()
    {
        // Arrange: misma OC recibida en dos obras distintas
        using var db = CreateInMemoryContext($"Db_Filtros_ObraYOC_{Guid.NewGuid()}");
        var oc = Guid.NewGuid();
        var obraBuscada = Guid.NewGuid();
        var obraOtra = Guid.NewGuid();
        var idEsperado = Guid.NewGuid();
        var idOtraObra = Guid.NewGuid();
        db.Recepciones.AddRange(
            CrearRecepcion(idEsperado, obraBuscada, oc, "R-OBRA-A"),
            CrearRecepcion(idOtraObra, obraOtra, oc, "R-OBRA-B"));
        await db.SaveChangesAsync();
        var controller = CrearController(db);

        // Act
        var resultado = await controller.ObtenerRecepciones(
            obraBuscada, CancellationToken.None, ordenCompraId: oc);

        // Assert
        var lista = ExtraerLista(resultado);
        var unica = Assert.Single(lista);
        Assert.Equal(idEsperado, unica.Id);
        Assert.Equal(obraBuscada, unica.ObraId);
        Assert.Equal(oc, unica.OrdenCompraId);
        Assert.DoesNotContain(lista, r => r.Id == idOtraObra);
    }
}

