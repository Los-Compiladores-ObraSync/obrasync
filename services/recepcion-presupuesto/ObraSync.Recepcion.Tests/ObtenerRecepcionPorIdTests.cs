using ObraSync.Recepcion.Api.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ObraSync.Recepcion.Api.Controllers;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Infrastructure.Persistence;
using RecepcionEntity = ObraSync.Recepcion.Domain.Entities.Recepcion;

namespace ObraSync.Recepcion.Tests;

public class ObtenerRecepcionPorIdTests
{
    // Mismo patrón que RecepcionesControllerTests: DbContext con base InMemory
    // (Include/AsNoTracking/FirstOrDefaultAsync no se pueden simular con NSubstitute)
    // y publisher simulado con NSubstitute.
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

    [Fact]
    public async Task ObtenerRecepcionPorId_IdInexistente_Devuelve404()
    {
        // Arrange
        using var db = CreateInMemoryContext($"Db_PorId_404_{Guid.NewGuid()}");
        db.Recepciones.Add(new RecepcionEntity { Id = Guid.NewGuid(), ObraId = Guid.NewGuid(), NumeroRemito = "R1" });
        await db.SaveChangesAsync();
        var controller = CrearController(db);

        // Act
        var resultado = await controller.ObtenerRecepcionPorId(Guid.NewGuid(), CancellationToken.None);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(resultado);
        Assert.Equal(404, objectResult.StatusCode);
        var problema = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal("La recepción no existe.", problema.Detail);
    }

    [Fact]
    public async Task ObtenerRecepcionPorId_IdExistente_Devuelve200ConLaRecepcion()
    {
        // Arrange
        using var db = CreateInMemoryContext($"Db_PorId_200_{Guid.NewGuid()}");
        var recepcionId = Guid.NewGuid();
        var recepcion = new RecepcionEntity { Id = recepcionId, ObraId = Guid.NewGuid(), NumeroRemito = "REM-200" };
        recepcion.Detalles.Add(new DetalleRecepcion
        {
            Id = Guid.NewGuid(),
            RecepcionId = recepcionId,
            DescripcionArticulo = "Cemento",
            UnidadMedida = "BOLSA"
        });
        db.Recepciones.Add(recepcion);
        db.Recepciones.Add(new RecepcionEntity { Id = Guid.NewGuid(), ObraId = Guid.NewGuid(), NumeroRemito = "OTRA" });
        await db.SaveChangesAsync();
        var controller = CrearController(db);

        // Act
        var resultado = await controller.ObtenerRecepcionPorId(recepcionId, CancellationToken.None);

        // Assert
        var ok = Assert.IsType<OkObjectResult>(resultado);
        var devuelta = Assert.IsType<RecepcionDto>(ok.Value);
        Assert.Equal(recepcionId, devuelta.Id);
        Assert.Equal("REM-200", devuelta.NumeroRemito);
        Assert.Single(devuelta.Detalles);
    }

    [Fact]
    public async Task ObtenerRecepcionPorId_MapeaDetallesAlDto()
    {
        // Arrange
        using var db = CreateInMemoryContext($"Db_PorId_Detalles_{Guid.NewGuid()}");
        var recepcionId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        var detalleId = Guid.NewGuid();
        var recepcion = new RecepcionEntity
        {
            Id = recepcionId,
            ObraId = Guid.NewGuid(),
            OrdenCompraId = Guid.NewGuid(),
            NumeroRemito = "REM-MAP-1"
        };
        recepcion.Detalles.Add(new DetalleRecepcion
        {
            Id = detalleId,
            RecepcionId = recepcionId,
            ArticuloId = articuloId,
            DescripcionArticulo = "Hierro 8mm",
            CantidadEsperada = 20,
            CantidadRecibida = 20,
            UnidadMedida = "BARRA",
            Conforme = true
        });
        db.Recepciones.Add(recepcion);
        await db.SaveChangesAsync();
        var controller = CrearController(db);

        // Act
        var resultado = await controller.ObtenerRecepcionPorId(recepcionId, CancellationToken.None);

        // Assert
        var ok = Assert.IsType<OkObjectResult>(resultado);
        var dto = Assert.IsType<RecepcionDto>(ok.Value);
        Assert.Equal(recepcionId, dto.Id);
        var detalleDto = Assert.Single(dto.Detalles);
        Assert.Equal(detalleId, detalleDto.Id);
        Assert.Equal(articuloId, detalleDto.ArticuloId);
        Assert.Equal(20, detalleDto.CantidadRecibida);
        Assert.Equal(20, detalleDto.CantidadEsperada);
        Assert.Equal("BARRA", detalleDto.UnidadMedida);
        Assert.Equal("Hierro 8mm", detalleDto.DescripcionArticulo);
    }
}