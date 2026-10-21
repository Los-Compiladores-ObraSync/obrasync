using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ObraSync.Recepcion.Api.Controllers;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Domain.Enums;
using ObraSync.Recepcion.Infrastructure.Persistence;
using RecepcionEntity = ObraSync.Recepcion.Domain.Entities.Recepcion;

namespace ObraSync.Recepcion.Tests;

public class RecepcionesControllerTests
{
    private static RecepcionDbContext CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<RecepcionDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new RecepcionDbContext(options);
    }

    [Fact]
    public async Task ObtenerRecepciones_SinFiltro_RetornaTodasLasRecepciones()
    {
        using var db = CreateInMemoryContext($"Db_SinFiltro_{Guid.NewGuid()}");
        var obra1 = Guid.NewGuid();
        var obra2 = Guid.NewGuid();

        db.Recepciones.AddRange(
            new RecepcionEntity { Id = Guid.NewGuid(), ObraId = obra1, NumeroRemito = "R1", Estado = EstadoRecepcion.Pendiente },
            new RecepcionEntity { Id = Guid.NewGuid(), ObraId = obra2, NumeroRemito = "R2", Estado = EstadoRecepcion.Completa }
        );
        await db.SaveChangesAsync();

        var controller = new RecepcionesController(db, NullLogger<RecepcionesController>.Instance);
        var actionResult = await controller.ObtenerRecepciones(null, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var lista = Assert.IsAssignableFrom<List<RecepcionEntity>>(okResult.Value);
        Assert.Equal(2, lista.Count);
    }

    [Fact]
    public async Task ObtenerRecepciones_ConFiltroObraId_RetornaSoloRecepcionesFiltradas()
    {
        using var db = CreateInMemoryContext($"Db_ConFiltro_{Guid.NewGuid()}");
        var obraTarget = Guid.NewGuid();
        var obraOtra = Guid.NewGuid();

        db.Recepciones.AddRange(
            new RecepcionEntity { Id = Guid.NewGuid(), ObraId = obraTarget, NumeroRemito = "TARGET-1", Estado = EstadoRecepcion.Pendiente },
            new RecepcionEntity { Id = Guid.NewGuid(), ObraId = obraTarget, NumeroRemito = "TARGET-2", Estado = EstadoRecepcion.Parcial },
            new RecepcionEntity { Id = Guid.NewGuid(), ObraId = obraOtra, NumeroRemito = "OTRA-1", Estado = EstadoRecepcion.Pendiente }
        );
        await db.SaveChangesAsync();

        var controller = new RecepcionesController(db, NullLogger<RecepcionesController>.Instance);
        var actionResult = await controller.ObtenerRecepciones(obraTarget, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var lista = Assert.IsAssignableFrom<List<RecepcionEntity>>(okResult.Value);
        Assert.Equal(2, lista.Count);
        Assert.All(lista, r => Assert.Equal(obraTarget, r.ObraId));
    }

    [Fact]
    public async Task ObtenerRecepciones_ConFiltroObraInexistente_RetornaListaVacia()
    {
        using var db = CreateInMemoryContext($"Db_Inexistente_{Guid.NewGuid()}");
        db.Recepciones.Add(new RecepcionEntity { Id = Guid.NewGuid(), ObraId = Guid.NewGuid(), NumeroRemito = "R1" });
        await db.SaveChangesAsync();

        var controller = new RecepcionesController(db, NullLogger<RecepcionesController>.Instance);
        var actionResult = await controller.ObtenerRecepciones(Guid.NewGuid(), CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var lista = Assert.IsAssignableFrom<List<RecepcionEntity>>(okResult.Value);
        Assert.Empty(lista);
    }

    [Fact]
    public async Task ObtenerRecepciones_ConCancellationTokenCancelado_LanzaOperationCanceledException()
    {
        using var db = CreateInMemoryContext($"Db_Cancel_{Guid.NewGuid()}");
        var controller = new RecepcionesController(db, NullLogger<RecepcionesController>.Instance);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await controller.ObtenerRecepciones(null, cts.Token);
        });
    }

    [Fact]
    public async Task ObtenerRecepciones_ConDetalles_DiagnosticoSerializacionCiclica()
    {
        using var db = CreateInMemoryContext($"Diagnostico_{Guid.NewGuid()}");
        var recepcion = new RecepcionEntity
        {
            Id = Guid.NewGuid(),
            ObraId = Guid.NewGuid(),
            NumeroRemito = "REM-TEST",
        };
        var detalle = new DetalleRecepcion
        {
            Id = Guid.NewGuid(),
            RecepcionId = recepcion.Id,
            DescripcionArticulo = "Item 1",
            UnidadMedida = "U"
        };
        recepcion.Detalles.Add(detalle);
        db.Recepciones.Add(recepcion);
        await db.SaveChangesAsync();

        var controller = new RecepcionesController(db, NullLogger<RecepcionesController>.Instance);
        var actionResult = await controller.ObtenerRecepciones(null, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var lista = Assert.IsAssignableFrom<List<RecepcionEntity>>(okResult.Value);
        Assert.Single(lista);
        Assert.Single(lista[0].Detalles);

        // Al intentar serializar el resultado con las opciones por defecto de System.Text.Json en ASP.NET Core:
        var ex = Record.Exception(() => System.Text.Json.JsonSerializer.Serialize(okResult.Value));
        Assert.Null(ex); // El comportamiento esperado es que serialice correctamente sin lanzar excepción
    }
}
