using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Commands;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Handlers;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Domain.Enums;
using ObraSync.Recepcion.Infrastructure.Persistence;
using RecepcionEntity = ObraSync.Recepcion.Domain.Entities.Recepcion;

namespace ObraSync.Recepcion.Tests;

public class RecepcionOutboxTests
{
    private static RecepcionDbContext CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<RecepcionDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new RecepcionDbContext(options);
    }

    private static (OrdenCompra oc, Guid obraId, Guid articuloId) SembrarOrdenCompra(RecepcionDbContext db)
    {
        var obraId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        var item = OrdenCompraItem.Crear(articuloId, 50m, "UNIDAD", 100m, "Hierro 10mm");
        var oc = OrdenCompra.Crear(Guid.NewGuid(), obraId, Guid.NewGuid(), 5000m, new List<OrdenCompraItem> { item });

        db.OrdenesCompra.Add(oc);
        db.SaveChanges();

        return (oc, obraId, articuloId);
    }

    private static CrearRecepcionCommand CrearComando(
        Guid obraId,
        Guid ordenCompraId,
        Guid articuloId,
        Guid correlationId)
    {
        return new CrearRecepcionCommand(
            ObraId: obraId,
            OrdenCompraId: ordenCompraId,
            UsuarioId: Guid.NewGuid(),
            NumeroRemito: "REM-OUTBOX-1",
            FechaRecepcion: DateTime.UtcNow,
            Observaciones: "Prueba de Outbox Transaccional",
            Detalles: new List<CrearDetalleRecepcionRequest>
            {
                new()
                {
                    ArticuloId = articuloId,
                    DescripcionArticulo = "Hierro 10mm",
                    CantidadEsperada = 50m,
                    CantidadRecibida = 50m,
                    UnidadMedida = "UNIDAD",
                    Conforme = true
                }
            },
            CorrelationId: correlationId);
    }

    [Fact]
    public async Task Publica_el_evento_ANTES_de_guardar_para_enrolar_en_el_outbox_transaccional()
    {
        // Arrange
        using var db = CreateInMemoryContext($"Outbox_Order_{Guid.NewGuid()}");
        var (oc, obraId, articuloId) = SembrarOrdenCompra(db);

        var publisher = Substitute.For<IRecepcionEventPublisher>();
        var correlationId = Guid.NewGuid();
        var command = CrearComando(obraId, oc.Id, articuloId, correlationId);

        var callOrder = new List<string>();

        publisher.When(p => p.PublishRecepcionConfirmadaAsync(
                Arg.Any<RecepcionEntity>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()))
            .Do(_ =>
            {
                callOrder.Add("Publish");
                // En este punto del outbox, la recepción ya fue agregada al ChangeTracker pero todavía no persistida
                Assert.Equal(EntityState.Added, db.Entry(db.Recepciones.Local.Single()).State);
            });

        var handler = new CrearRecepcionCommandHandler(
            db,
            publisher,
            NullLogger<CrearRecepcionCommandHandler>.Instance);

        // Act
        var resultado = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.Single(callOrder);
        Assert.Equal("Publish", callOrder[0]);

        // Luego de completar Handle, la recepción ya está persistida (Unchanged)
        Assert.Equal(EntityState.Unchanged, db.Entry(db.Recepciones.Local.Single()).State);
        Assert.Equal(resultado.Id, db.Recepciones.Local.Single().Id);

        await publisher.Received(1).PublishRecepcionConfirmadaAsync(
            Arg.Is<RecepcionEntity>(r => r.Id == resultado.Id && r.NumeroRemito == "REM-OUTBOX-1"),
            correlationId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Si_publicar_falla_la_recepcion_no_se_persiste_y_no_queda_guardada_sin_evento()
    {
        // Arrange
        using var db = CreateInMemoryContext($"Outbox_FailPublish_{Guid.NewGuid()}");
        var (oc, obraId, articuloId) = SembrarOrdenCompra(db);

        var publisher = Substitute.For<IRecepcionEventPublisher>();
        publisher.PublishRecepcionConfirmadaAsync(
                Arg.Any<RecepcionEntity>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("Fallo en bus o outbox")));

        var handler = new CrearRecepcionCommandHandler(
            db,
            publisher,
            NullLogger<CrearRecepcionCommandHandler>.Instance);

        var command = CrearComando(obraId, oc.Id, articuloId, Guid.NewGuid());

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(command, CancellationToken.None));

        // Verificamos que la recepción no fue persistida en la base de datos
        Assert.Equal(0, await db.Recepciones.CountAsync());
    }

    [Fact]
    public async Task Evento_incluye_todos_los_datos_necesarios_para_consumidores_asincronos()
    {
        // Arrange
        using var db = CreateInMemoryContext($"Outbox_Payload_{Guid.NewGuid()}");
        var (oc, obraId, articuloId) = SembrarOrdenCompra(db);

        var publisher = Substitute.For<IRecepcionEventPublisher>();
        var correlationId = Guid.NewGuid();
        var command = CrearComando(obraId, oc.Id, articuloId, correlationId);

        RecepcionEntity? recepcionCapturada = null;
        await publisher.PublishRecepcionConfirmadaAsync(
            Arg.Do<RecepcionEntity>(r => recepcionCapturada = r),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());

        var handler = new CrearRecepcionCommandHandler(
            db,
            publisher,
            NullLogger<CrearRecepcionCommandHandler>.Instance);

        // Act
        var dto = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(recepcionCapturada);
        Assert.Equal(dto.Id, recepcionCapturada.Id);
        Assert.Equal(obraId, recepcionCapturada.ObraId);
        Assert.Equal(oc.Id, recepcionCapturada.OrdenCompraId);
        Assert.Equal("REM-OUTBOX-1", recepcionCapturada.NumeroRemito);
        Assert.Equal(EstadoRecepcion.Completa, recepcionCapturada.Estado);
        Assert.Single(recepcionCapturada.Detalles);
        Assert.Equal(articuloId, recepcionCapturada.Detalles.First().ArticuloId);
        Assert.Equal(50m, recepcionCapturada.Detalles.First().CantidadRecibida);
    }
}
