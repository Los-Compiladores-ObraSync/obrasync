using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Exceptions;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Commands;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Handlers;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Domain.Enums;
using ObraSync.Recepcion.Infrastructure.Persistence;
using RecepcionEntity = ObraSync.Recepcion.Domain.Entities.Recepcion;

namespace ObraSync.Recepcion.Tests.Application;

public class CrearRecepcionCommandHandlerTests
{
    private static RecepcionDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<RecepcionDbContext>()
            .UseInMemoryDatabase($"Handler_{Guid.NewGuid()}")
            .Options;
        return new RecepcionDbContext(options);
    }

    private static (OrdenCompra oc, Guid obraId, Guid articuloId) SembrarOrdenCompra(
        RecepcionDbContext db,
        decimal cantidad = 100m)
    {
        var obraId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        var item = OrdenCompraItem.Crear(articuloId, cantidad, "UNIDAD", 10m, "Barras de hierro");
        var oc = OrdenCompra.Crear(Guid.NewGuid(), obraId, Guid.NewGuid(), cantidad * 10m, new List<OrdenCompraItem> { item },
            fechaDespacho: DateTime.UtcNow);

        db.OrdenesCompra.Add(oc);
        db.SaveChanges();

        return (oc, obraId, articuloId);
    }

    private static CrearRecepcionCommand CrearComandoValido(
        Guid obraId,
        Guid ordenCompraId,
        Guid articuloId,
        Guid? usuarioId = null,
        string numeroRemito = "REM-0001",
        DateTime? fechaRecepcion = null,
        string? observaciones = "Entrega conforme",
        decimal cantidadEsperada = 100m,
        decimal cantidadRecibida = 100m,
        Guid correlationId = default)
    {
        return new CrearRecepcionCommand(
            ObraId: obraId,
            OrdenCompraId: ordenCompraId,
            UsuarioId: usuarioId ?? Guid.NewGuid(),
            NumeroRemito: numeroRemito,
            FechaRecepcion: fechaRecepcion ?? DateTime.UtcNow,
            Observaciones: observaciones,
            Detalles: new List<CrearDetalleRecepcionRequest>
            {
                new()
                {
                    ArticuloId = articuloId,
                    DescripcionArticulo = "Barras de hierro",
                    CantidadEsperada = cantidadEsperada,
                    CantidadRecibida = cantidadRecibida,
                    UnidadMedida = "UNIDAD",
                    Conforme = true
                }
            },
            CorrelationId: correlationId);
    }

    [Fact]
    public async Task Handle_ComandoValido_GuardaRecepcionCompletaYPublicaEvento()
    {
        using var db = CreateInMemoryContext();
        var (oc, obraId, articuloId) = SembrarOrdenCompra(db);
        var publisher = Substitute.For<IRecepcionEventPublisher>();
        var handler = new CrearRecepcionCommandHandler(
            db,
            publisher,
            NullLogger<CrearRecepcionCommandHandler>.Instance);

        var usuarioId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var command = CrearComandoValido(
            obraId,
            oc.Id,
            articuloId,
            usuarioId: usuarioId,
            numeroRemito: "  REM-100  ",
            correlationId: correlationId);

        var resultado = await handler.Handle(command, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, resultado.Id);
        Assert.Equal(obraId, resultado.ObraId);
        Assert.Equal(oc.Id, resultado.OrdenCompraId);
        Assert.Equal(usuarioId, resultado.RecepcionadoPorUsuarioId);
        Assert.Equal("REM-100", resultado.NumeroRemito);
        Assert.Equal(EstadoRecepcion.Completa, resultado.Estado);
        Assert.Single(resultado.Detalles);

        var guardada = await db.Recepciones.Include(r => r.Detalles).SingleAsync();
        Assert.Equal(resultado.Id, guardada.Id);
        Assert.Equal(EstadoRecepcion.Completa, guardada.Estado);

        await publisher.Received(1).PublishRecepcionConfirmadaAsync(
            Arg.Is<RecepcionEntity>(r => r.Id == resultado.Id && r.NumeroRemito == "REM-100"),
            correlationId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UsuarioIdVacio_LanzaArgumentExceptionSinConsultarDbNiPublicar()
    {
        var db = Substitute.For<IRecepcionDbContext>();
        var publisher = Substitute.For<IRecepcionEventPublisher>();
        var handler = new CrearRecepcionCommandHandler(
            db,
            publisher,
            NullLogger<CrearRecepcionCommandHandler>.Instance);

        var command = CrearComandoValido(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            usuarioId: Guid.Empty);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(command, CancellationToken.None));

        Assert.Equal("El usuario que registra la recepción es obligatorio.", ex.Message);
        await publisher.DidNotReceiveWithAnyArgs().PublishRecepcionConfirmadaAsync(default!, default, default);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Handle_ObraUOrdenVacia_LanzaArgumentException(bool obraVacia, bool ordenVacia)
    {
        var db = Substitute.For<IRecepcionDbContext>();
        var publisher = Substitute.For<IRecepcionEventPublisher>();
        var handler = new CrearRecepcionCommandHandler(
            db,
            publisher,
            NullLogger<CrearRecepcionCommandHandler>.Instance);

        var command = CrearComandoValido(
            obraVacia ? Guid.Empty : Guid.NewGuid(),
            ordenVacia ? Guid.Empty : Guid.NewGuid(),
            Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(command, CancellationToken.None));

        Assert.Equal("La obra y la orden de compra son obligatorias.", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123456789012345678901234567890123456789012345678901")]
    public async Task Handle_NumeroRemitoInvalido_LanzaArgumentException(string remito)
    {
        var db = Substitute.For<IRecepcionDbContext>();
        var publisher = Substitute.For<IRecepcionEventPublisher>();
        var handler = new CrearRecepcionCommandHandler(
            db,
            publisher,
            NullLogger<CrearRecepcionCommandHandler>.Instance);

        var command = CrearComandoValido(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            numeroRemito: remito);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(command, CancellationToken.None));

        Assert.Equal("El número de remito es obligatorio y no puede superar los 50 caracteres.", ex.Message);
    }

    [Fact]
    public async Task Handle_FechaRecepcionDefault_LanzaArgumentException()
    {
        var db = Substitute.For<IRecepcionDbContext>();
        var publisher = Substitute.For<IRecepcionEventPublisher>();
        var handler = new CrearRecepcionCommandHandler(
            db,
            publisher,
            NullLogger<CrearRecepcionCommandHandler>.Instance);

        var command = CrearComandoValido(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            fechaRecepcion: default(DateTime));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(command, CancellationToken.None));

        Assert.Equal("La fecha de recepción es obligatoria.", ex.Message);
    }

    [Fact]
    public async Task Handle_FechaRecepcionFutura_LanzaArgumentException()
    {
        var db = Substitute.For<IRecepcionDbContext>();
        var publisher = Substitute.For<IRecepcionEventPublisher>();
        var handler = new CrearRecepcionCommandHandler(
            db,
            publisher,
            NullLogger<CrearRecepcionCommandHandler>.Instance);

        var command = CrearComandoValido(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            fechaRecepcion: DateTime.UtcNow.Date.AddDays(2));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(command, CancellationToken.None));

        Assert.Equal("La fecha de recepcion no puede ser futura.", ex.Message);
    }

    [Fact]
    public async Task Handle_ObservacionesSuperan500Caracteres_LanzaArgumentException()
    {
        var db = Substitute.For<IRecepcionDbContext>();
        var publisher = Substitute.For<IRecepcionEventPublisher>();
        var handler = new CrearRecepcionCommandHandler(
            db,
            publisher,
            NullLogger<CrearRecepcionCommandHandler>.Instance);

        var command = CrearComandoValido(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            observaciones: new string('A', 501));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(command, CancellationToken.None));

        Assert.Equal("Las observaciones no pueden superar los 500 caracteres.", ex.Message);
    }

    [Fact]
    public async Task Handle_SinDetalles_LanzaArgumentException()
    {
        var db = Substitute.For<IRecepcionDbContext>();
        var publisher = Substitute.For<IRecepcionEventPublisher>();
        var handler = new CrearRecepcionCommandHandler(
            db,
            publisher,
            NullLogger<CrearRecepcionCommandHandler>.Instance);

        var command = new CrearRecepcionCommand(
            ObraId: Guid.NewGuid(),
            OrdenCompraId: Guid.NewGuid(),
            UsuarioId: Guid.NewGuid(),
            NumeroRemito: "REM-1",
            FechaRecepcion: DateTime.UtcNow,
            Observaciones: null,
            Detalles: Array.Empty<CrearDetalleRecepcionRequest>());

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(command, CancellationToken.None));

        Assert.Equal("La recepción debe incluir al menos un material.", ex.Message);
    }

    [Fact]
    public async Task Handle_OrdenCompraInexistente_LanzaArgumentException()
    {
        using var db = CreateInMemoryContext();
        var publisher = Substitute.For<IRecepcionEventPublisher>();
        var handler = new CrearRecepcionCommandHandler(
            db,
            publisher,
            NullLogger<CrearRecepcionCommandHandler>.Instance);

        var command = CrearComandoValido(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(command, CancellationToken.None));

        Assert.Equal("La orden de compra no existe.", ex.Message);
    }

    [Fact]
    public async Task Handle_OrdenCompraDeOtraObra_LanzaArgumentException()
    {
        using var db = CreateInMemoryContext();
        var (oc, _, articuloId) = SembrarOrdenCompra(db);
        var publisher = Substitute.For<IRecepcionEventPublisher>();
        var handler = new CrearRecepcionCommandHandler(
            db,
            publisher,
            NullLogger<CrearRecepcionCommandHandler>.Instance);

        var otraObraId = Guid.NewGuid();
        var command = CrearComandoValido(otraObraId, oc.Id, articuloId);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(command, CancellationToken.None));

        Assert.Equal("La orden de compra no pertenece a la obra indicada.", ex.Message);
    }

    [Fact]
    public async Task Handle_OrdenCompraYaRecibidaCompleta_LanzaRecepcionConflictoException()
    {
        using var db = CreateInMemoryContext();
        var (oc, obraId, articuloId) = SembrarOrdenCompra(db);
        db.Recepciones.Add(new RecepcionEntity
        {
            Id = Guid.NewGuid(),
            ObraId = obraId,
            OrdenCompraId = oc.Id,
            RecepcionadoPorUsuarioId = Guid.NewGuid(),
            NumeroRemito = "REM-PREVIA",
            Estado = EstadoRecepcion.Completa
        });
        await db.SaveChangesAsync();

        var publisher = Substitute.For<IRecepcionEventPublisher>();
        var handler = new CrearRecepcionCommandHandler(
            db,
            publisher,
            NullLogger<CrearRecepcionCommandHandler>.Instance);

        var command = CrearComandoValido(obraId, oc.Id, articuloId, numeroRemito: "REM-NUEVA");

        var ex = await Assert.ThrowsAsync<RecepcionConflictoException>(() =>
            handler.Handle(command, CancellationToken.None));

        Assert.Equal("La orden de compra ya fue recibida por completo.", ex.Message);
    }

    [Fact]
    public async Task Handle_RemitoRepetidoEnMismaOC_LanzaRecepcionConflictoException()
    {
        using var db = CreateInMemoryContext();
        var (oc, obraId, articuloId) = SembrarOrdenCompra(db);
        db.Recepciones.Add(new RecepcionEntity
        {
            Id = Guid.NewGuid(),
            ObraId = obraId,
            OrdenCompraId = oc.Id,
            RecepcionadoPorUsuarioId = Guid.NewGuid(),
            NumeroRemito = "REM-DUP",
            Estado = EstadoRecepcion.Parcial
        });
        await db.SaveChangesAsync();

        var publisher = Substitute.For<IRecepcionEventPublisher>();
        var handler = new CrearRecepcionCommandHandler(
            db,
            publisher,
            NullLogger<CrearRecepcionCommandHandler>.Instance);

        var command = CrearComandoValido(obraId, oc.Id, articuloId, numeroRemito: "  REM-DUP ");

        var ex = await Assert.ThrowsAsync<RecepcionConflictoException>(() =>
            handler.Handle(command, CancellationToken.None));

        Assert.Equal("Ya existe una recepción con ese número de remito para esta orden de compra.", ex.Message);
    }

    [Fact]
    public async Task Handle_ArticulosRepetidosEnDetalle_LanzaArgumentException()
    {
        using var db = CreateInMemoryContext();
        var obraId = Guid.NewGuid();
        var art1 = Guid.NewGuid();
        var art2 = Guid.NewGuid();
        var oc = OrdenCompra.Crear(
            Guid.NewGuid(),
            obraId,
            Guid.NewGuid(),
            2000m,
            new List<OrdenCompraItem>
            {
                OrdenCompraItem.Crear(art1, 10m, "UN", 100m, "Cemento"),
                OrdenCompraItem.Crear(art2, 10m, "UN", 100m, "Cal")
            },
            fechaDespacho: DateTime.UtcNow);
        db.OrdenesCompra.Add(oc);
        await db.SaveChangesAsync();

        var publisher = Substitute.For<IRecepcionEventPublisher>();
        var handler = new CrearRecepcionCommandHandler(
            db,
            publisher,
            NullLogger<CrearRecepcionCommandHandler>.Instance);

        var command = new CrearRecepcionCommand(
            ObraId: obraId,
            OrdenCompraId: oc.Id,
            UsuarioId: Guid.NewGuid(),
            NumeroRemito: "REM-REP",
            FechaRecepcion: DateTime.UtcNow,
            Observaciones: null,
            Detalles: new List<CrearDetalleRecepcionRequest>
            {
                new() { ArticuloId = art1, DescripcionArticulo = "Cemento", CantidadEsperada = 10m, CantidadRecibida = 10m, UnidadMedida = "UN" },
                new() { ArticuloId = art1, DescripcionArticulo = "Cemento", CantidadEsperada = 10m, CantidadRecibida = 10m, UnidadMedida = "UN" }
            });

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(command, CancellationToken.None));

        Assert.Equal("No se puede incluir un mismo artículo más de una vez.", ex.Message);
    }

    [Fact]
    public async Task Handle_SiPublisherLanzaExcepcion_NoGuardaRecepcionAsegurandoAtomicidad()
    {
        using var db = CreateInMemoryContext();
        var (oc, obraId, articuloId) = SembrarOrdenCompra(db);
        var publisher = Substitute.For<IRecepcionEventPublisher>();
        publisher.PublishRecepcionConfirmadaAsync(default!, default, default)
            .ReturnsForAnyArgs(Task.FromException(new InvalidOperationException("Fallo al publicar")));

        var logger = Substitute.For<ILogger<CrearRecepcionCommandHandler>>();
        var handler = new CrearRecepcionCommandHandler(db, publisher, logger);

        var command = CrearComandoValido(obraId, oc.Id, articuloId);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(command, CancellationToken.None));

        Assert.Equal(0, await db.Recepciones.CountAsync());
    }

    [Fact]
    public async Task Handle_SiOrdenCompraEstaBloqueada_LanzaRecepcionConflictoException()
    {
        // Arrange (#95, #96)
        using var db = CreateInMemoryContext();
        var (oc, obraId, articuloId) = SembrarOrdenCompra(db);
        oc.BloquearPorDesvioPresupuestario("Supera presupuesto asignado a la etapa");
        await db.SaveChangesAsync();

        var publisher = Substitute.For<IRecepcionEventPublisher>();
        var handler = new CrearRecepcionCommandHandler(db, publisher, NullLogger<CrearRecepcionCommandHandler>.Instance);

        var command = CrearComandoValido(obraId, oc.Id, articuloId);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<RecepcionConflictoException>(() =>
            handler.Handle(command, CancellationToken.None));

        Assert.Contains("bloqueada", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await db.Recepciones.CountAsync());
    }

    [Fact]
    public async Task Handle_SiOrdenCompraNoEstaDespachada_LanzaRecepcionConflictoExceptionConMensajeExacto()
    {
        using var db = CreateInMemoryContext();
        var obraId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        var item = OrdenCompraItem.Crear(articuloId, 100m, "UNIDAD", 10m, "Barras de hierro");
        var ocNoDespachada = OrdenCompra.Crear(
            Guid.NewGuid(),
            obraId,
            Guid.NewGuid(),
            1000m,
            new List<OrdenCompraItem> { item });

        db.OrdenesCompra.Add(ocNoDespachada);
        await db.SaveChangesAsync();

        var publisher = Substitute.For<IRecepcionEventPublisher>();
        var handler = new CrearRecepcionCommandHandler(db, publisher, NullLogger<CrearRecepcionCommandHandler>.Instance);

        var command = CrearComandoValido(obraId, ocNoDespachada.Id, articuloId);

        var ex = await Assert.ThrowsAsync<RecepcionConflictoException>(() =>
            handler.Handle(command, CancellationToken.None));

        Assert.Equal("La orden de compra todavía no fue despachada", ex.Message);
        Assert.Equal(0, await db.Recepciones.CountAsync());
    }

    [Fact]
    public async Task Handle_EntregaParcialSeguidaDeSaldo_PrimeraParcialSegundaCompleta()
    {
        using var db = CreateInMemoryContext();
        var (oc, obraId, articuloId) = SembrarOrdenCompra(db, cantidad: 100m);
        var publisher = Substitute.For<IRecepcionEventPublisher>();
        var handler = new CrearRecepcionCommandHandler(db, publisher, NullLogger<CrearRecepcionCommandHandler>.Instance);

        // 1. Primera entrega: 75 de 100 -> Parcial
        var cmd1 = CrearComandoValido(
            obraId, oc.Id, articuloId,
            numeroRemito: "REM-PARC-1",
            cantidadEsperada: 100m,
            cantidadRecibida: 75m);

        var res1 = await handler.Handle(cmd1, CancellationToken.None);
        Assert.Equal(EstadoRecepcion.Parcial, res1.Estado);
        Assert.Equal(75m, res1.Detalles.Single().CantidadRecibida);
        Assert.Equal(100m, res1.Detalles.Single().CantidadEsperada);

        // 2. Segunda entrega: 25 de 100 (usando cantidadEsperada = saldo pendiente 25) -> Completa
        var cmd2 = CrearComandoValido(
            obraId, oc.Id, articuloId,
            numeroRemito: "REM-PARC-2",
            cantidadEsperada: 25m,
            cantidadRecibida: 25m);

        var conflicto = await Assert.ThrowsAsync<RecepcionConflictoException>(
            () => handler.Handle(cmd2, CancellationToken.None));
        Assert.Contains("esperando que el proveedor despache", conflicto.Message);

        await RedespachoDePrueba.RegistrarAsync(db, oc.Id);

        var res2 = await handler.Handle(cmd2, CancellationToken.None);
        Assert.Equal(EstadoRecepcion.Completa, res2.Estado);
        Assert.Equal(25m, res2.Detalles.Single().CantidadRecibida);
        Assert.Equal(100m, res2.Detalles.Single().CantidadEsperada);

        await publisher.Received(2).PublishRecepcionConfirmadaAsync(
            Arg.Any<RecepcionEntity>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TerceraRecepcionTrasCompletarPorParciales_LanzaRecepcionConflictoException()
    {
        using var db = CreateInMemoryContext();
        var (oc, obraId, articuloId) = SembrarOrdenCompra(db, cantidad: 100m);
        var publisher = Substitute.For<IRecepcionEventPublisher>();
        var handler = new CrearRecepcionCommandHandler(db, publisher, NullLogger<CrearRecepcionCommandHandler>.Instance);

        await handler.Handle(
            CrearComandoValido(obraId, oc.Id, articuloId, numeroRemito: "REM-1", cantidadEsperada: 100m, cantidadRecibida: 75m),
            CancellationToken.None);

        await RedespachoDePrueba.RegistrarAsync(db, oc.Id);

        await handler.Handle(
            CrearComandoValido(obraId, oc.Id, articuloId, numeroRemito: "REM-2", cantidadEsperada: 100m, cantidadRecibida: 25m),
            CancellationToken.None);

        var ex = await Assert.ThrowsAsync<RecepcionConflictoException>(() =>
            handler.Handle(
                CrearComandoValido(obraId, oc.Id, articuloId, numeroRemito: "REM-3", cantidadEsperada: 100m, cantidadRecibida: 10m),
                CancellationToken.None));

        Assert.Equal("La orden de compra ya fue recibida por completo.", ex.Message);
    }

    [Fact]
    public async Task Handle_ExcesoAcumuladoEnSegundaEntrega_LanzaArgumentExceptionSinGuardarNiPublicarSegundoEvento()
    {
        using var db = CreateInMemoryContext();
        var (oc, obraId, articuloId) = SembrarOrdenCompra(db, cantidad: 100m);
        var publisher = Substitute.For<IRecepcionEventPublisher>();
        var handler = new CrearRecepcionCommandHandler(db, publisher, NullLogger<CrearRecepcionCommandHandler>.Instance);

        await handler.Handle(
            CrearComandoValido(obraId, oc.Id, articuloId, numeroRemito: "REM-1", cantidadEsperada: 100m, cantidadRecibida: 75m),
            CancellationToken.None);

        await RedespachoDePrueba.RegistrarAsync(db, oc.Id);

        // Segunda entrega con 30 (75 + 30 = 105 > 100)
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(
                CrearComandoValido(obraId, oc.Id, articuloId, numeroRemito: "REM-2", cantidadEsperada: 100m, cantidadRecibida: 30m),
                CancellationToken.None));

        Assert.Equal("La cantidad recibida acumulada supera la cantidad pedida en la orden de compra.", ex.Message);
        Assert.Equal(1, await db.Recepciones.CountAsync());
        await publisher.Received(1).PublishRecepcionConfirmadaAsync(
            Arg.Any<RecepcionEntity>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DosArticulos_CompletadoEnDosEntregas_PersisteCantidadEsperadaDeOC()
    {
        using var db = CreateInMemoryContext();
        var obraId = Guid.NewGuid();
        var art1 = Guid.NewGuid();
        var art2 = Guid.NewGuid();

        var oc = OrdenCompra.Crear(
            Guid.NewGuid(),
            obraId,
            Guid.NewGuid(),
            2000m,
            new List<OrdenCompraItem>
            {
                OrdenCompraItem.Crear(art1, 100m, "UN", 10m, "Hierro"),
                OrdenCompraItem.Crear(art2, 50m, "UN", 20m, "Cemento")
            },
            fechaDespacho: DateTime.UtcNow);
        db.OrdenesCompra.Add(oc);
        await db.SaveChangesAsync();

        var publisher = Substitute.For<IRecepcionEventPublisher>();
        var handler = new CrearRecepcionCommandHandler(db, publisher, NullLogger<CrearRecepcionCommandHandler>.Instance);

        // Entrega 1: 100/100 de art1 y 20/50 de art2 -> Parcial
        var cmd1 = new CrearRecepcionCommand(
            ObraId: obraId,
            OrdenCompraId: oc.Id,
            UsuarioId: Guid.NewGuid(),
            NumeroRemito: "REM-M1",
            FechaRecepcion: DateTime.UtcNow,
            Observaciones: null,
            Detalles: new List<CrearDetalleRecepcionRequest>
            {
                new() { ArticuloId = art1, DescripcionArticulo = "Hierro", CantidadEsperada = 100m, CantidadRecibida = 100m, UnidadMedida = "UN", Conforme = true },
                new() { ArticuloId = art2, DescripcionArticulo = "Cemento", CantidadEsperada = 50m, CantidadRecibida = 20m, UnidadMedida = "UN", Conforme = true }
            });

        var res1 = await handler.Handle(cmd1, CancellationToken.None);
        Assert.Equal(EstadoRecepcion.Parcial, res1.Estado);

        // Entrega 2: 0 de art1 y 30 de art2 -> Completa
        var cmd2 = new CrearRecepcionCommand(
            ObraId: obraId,
            OrdenCompraId: oc.Id,
            UsuarioId: Guid.NewGuid(),
            NumeroRemito: "REM-M2",
            FechaRecepcion: DateTime.UtcNow,
            Observaciones: null,
            Detalles: new List<CrearDetalleRecepcionRequest>
            {
                new() { ArticuloId = art1, DescripcionArticulo = "Hierro", CantidadEsperada = 100m, CantidadRecibida = 0m, UnidadMedida = "UN", Conforme = true },
                new() { ArticuloId = art2, DescripcionArticulo = "Cemento", CantidadEsperada = 30m, CantidadRecibida = 30m, UnidadMedida = "UN", Conforme = true }
            });

        await RedespachoDePrueba.RegistrarAsync(db, oc.Id);

        var res2 = await handler.Handle(cmd2, CancellationToken.None);
        Assert.Equal(EstadoRecepcion.Completa, res2.Estado);
        Assert.Equal(100m, res2.Detalles.Single(d => d.ArticuloId == art1).CantidadEsperada);
        Assert.Equal(50m, res2.Detalles.Single(d => d.ArticuloId == art2).CantidadEsperada);
    }

    [Fact]
    public async Task Handle_TodosLosItemsConCantidadRecibidaCero_LanzaArgumentException()
    {
        using var db = CreateInMemoryContext();
        var (oc, obraId, articuloId) = SembrarOrdenCompra(db, cantidad: 100m);
        var publisher = Substitute.For<IRecepcionEventPublisher>();
        var handler = new CrearRecepcionCommandHandler(db, publisher, NullLogger<CrearRecepcionCommandHandler>.Instance);

        var cmd = CrearComandoValido(
            obraId, oc.Id, articuloId,
            numeroRemito: "REM-CERO",
            cantidadEsperada: 100m,
            cantidadRecibida: 0m);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(cmd, CancellationToken.None));

        Assert.Equal(0, await db.Recepciones.CountAsync());
        await publisher.DidNotReceiveWithAnyArgs().PublishRecepcionConfirmadaAsync(default!, default, default);
    }

    [Fact]
    public async Task Handle_CantidadRecibidaNegativa_LanzaArgumentException()
    {
        using var db = CreateInMemoryContext();
        var (oc, obraId, articuloId) = SembrarOrdenCompra(db, cantidad: 100m);
        var publisher = Substitute.For<IRecepcionEventPublisher>();
        var handler = new CrearRecepcionCommandHandler(db, publisher, NullLogger<CrearRecepcionCommandHandler>.Instance);

        var cmd = CrearComandoValido(
            obraId, oc.Id, articuloId,
            numeroRemito: "REM-NEG",
            cantidadEsperada: 100m,
            cantidadRecibida: -5m);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(cmd, CancellationToken.None));

        Assert.Equal("La cantidad recibida no puede ser negativa.", ex.Message);
        Assert.Equal(0, await db.Recepciones.CountAsync());
    }
}
