using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using ObraSync.Contracts.Events;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Infrastructure.Consumers;
using ObraSync.Recepcion.Infrastructure.Persistence;
using ObraSync.Recepcion.Infrastructure.Persistence.Repositories;
using Xunit;

namespace ObraSync.Recepcion.Tests.Consumers;

public class OrdenCompraEmitidaConsumerTests
{
    private RecepcionDbContext CreateDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<RecepcionDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new RecepcionDbContext(options);
    }

    private ConsumeContext<OrdenCompraEmitidaEvent> CreateConsumeContext(
        OrdenCompraEmitidaEvent message,
        CancellationToken cancellationToken = default)
    {
        var context = Substitute.For<ConsumeContext<OrdenCompraEmitidaEvent>>();
        context.Message.Returns(message);
        context.CancellationToken.Returns(cancellationToken);
        return context;
    }

    [Fact]
    public async Task CasoA_EventoValido_OcNueva_PersisteOcYEventoProcesado()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        var repo = new OrdenCompraRepository(db);
        var logger = Substitute.For<ILogger<OrdenCompraEmitidaConsumer>>();
        var consumer = new OrdenCompraEmitidaConsumer(db, repo, logger);

        var evento = new OrdenCompraEmitidaEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow,
            CorrelationId: Guid.NewGuid(),
            OrdenCompraId: Guid.NewGuid(),
            LicitacionId: Guid.NewGuid(),
            RequisicionId: Guid.NewGuid(),
            ObraId: Guid.NewGuid(),
            ProveedorId: Guid.NewGuid(),
            MontoTotal: 1500m,
            Items: new List<OrdenCompraEmitidaItemEvent>
            {
                new(Guid.NewGuid(), 10m, "KG", 100m, "Cemento"),
                new(Guid.NewGuid(), 5m, "M3", 100m, "Arena")
            });

        var context = CreateConsumeContext(evento);

        // Act
        await consumer.Consume(context);

        // Assert
        var ocGuardada = await db.OrdenesCompra
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == evento.OrdenCompraId);

        Assert.NotNull(ocGuardada);
        Assert.Equal(evento.ObraId, ocGuardada.ObraId);
        Assert.Equal(evento.ProveedorId, ocGuardada.ProveedorId);
        Assert.Equal(evento.MontoTotal, ocGuardada.MontoTotal);
        Assert.Equal(2, ocGuardada.Items.Count);

        var eventoGuardado = await db.EventosProcesados.FirstOrDefaultAsync(e => e.EventId == evento.EventId);
        Assert.NotNull(eventoGuardado);
        Assert.Equal(nameof(OrdenCompraEmitidaEvent), eventoGuardado.TipoEvento);
    }

    [Fact]
    public async Task CasoB_MismoEventIdDosVeces_SegundoLlamadoNoDuplicaNada()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        var repo = new OrdenCompraRepository(db);
        var logger = Substitute.For<ILogger<OrdenCompraEmitidaConsumer>>();
        var consumer = new OrdenCompraEmitidaConsumer(db, repo, logger);

        var evento = new OrdenCompraEmitidaEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow,
            CorrelationId: Guid.NewGuid(),
            OrdenCompraId: Guid.NewGuid(),
            LicitacionId: Guid.NewGuid(),
            RequisicionId: Guid.NewGuid(),
            ObraId: Guid.NewGuid(),
            ProveedorId: Guid.NewGuid(),
            MontoTotal: 1000m,
            Items: new List<OrdenCompraEmitidaItemEvent>
            {
                new(Guid.NewGuid(), 10m, "UN", 100m, "Ladrillo")
            });

        var context = CreateConsumeContext(evento);

        // Act: primer procesamiento exitoso
        await consumer.Consume(context);
        var countOcPrimerPaso = await db.OrdenesCompra.CountAsync();
        var countEventosPrimerPaso = await db.EventosProcesados.CountAsync();

        // Act: segundo procesamiento con el mismo evento
        await consumer.Consume(context);

        // Assert
        Assert.Equal(1, countOcPrimerPaso);
        Assert.Equal(1, countEventosPrimerPaso);
        Assert.Equal(1, await db.OrdenesCompra.CountAsync());
        Assert.Equal(1, await db.EventosProcesados.CountAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CasoC_EventoInvalido_CantidadInvalida_RetornaSinPersistir(decimal cantidadInvalida)
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        var repo = new OrdenCompraRepository(db);
        var logger = Substitute.For<ILogger<OrdenCompraEmitidaConsumer>>();
        var consumer = new OrdenCompraEmitidaConsumer(db, repo, logger);

        var evento = new OrdenCompraEmitidaEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow,
            CorrelationId: Guid.NewGuid(),
            OrdenCompraId: Guid.NewGuid(),
            LicitacionId: Guid.NewGuid(),
            RequisicionId: Guid.NewGuid(),
            ObraId: Guid.NewGuid(),
            ProveedorId: Guid.NewGuid(),
            MontoTotal: 1000m,
            Items: new List<OrdenCompraEmitidaItemEvent>
            {
                new(Guid.NewGuid(), cantidadInvalida, "UN", 100m, "Ladrillo")
            });

        var context = CreateConsumeContext(evento);

        // Act
        await consumer.Consume(context);

        // Assert
        Assert.Equal(0, await db.OrdenesCompra.CountAsync());
        Assert.Equal(0, await db.EventosProcesados.CountAsync());
    }

    [Fact]
    public async Task CasoC_EventoInvalido_ItemsVacios_RetornaSinPersistir()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        var repo = new OrdenCompraRepository(db);
        var logger = Substitute.For<ILogger<OrdenCompraEmitidaConsumer>>();
        var consumer = new OrdenCompraEmitidaConsumer(db, repo, logger);

        var evento = new OrdenCompraEmitidaEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow,
            CorrelationId: Guid.NewGuid(),
            OrdenCompraId: Guid.NewGuid(),
            LicitacionId: Guid.NewGuid(),
            RequisicionId: Guid.NewGuid(),
            ObraId: Guid.NewGuid(),
            ProveedorId: Guid.NewGuid(),
            MontoTotal: 1000m,
            Items: new List<OrdenCompraEmitidaItemEvent>());

        var context = CreateConsumeContext(evento);

        // Act
        await consumer.Consume(context);

        // Assert
        Assert.Equal(0, await db.OrdenesCompra.CountAsync());
        Assert.Equal(0, await db.EventosProcesados.CountAsync());
    }

    [Fact]
    public async Task CasoD_ItemsDuplicadosPorArticuloId_RetornaSinPersistir()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        var repo = new OrdenCompraRepository(db);
        var logger = Substitute.For<ILogger<OrdenCompraEmitidaConsumer>>();
        var consumer = new OrdenCompraEmitidaConsumer(db, repo, logger);

        var articuloDuplicadoId = Guid.NewGuid();
        var evento = new OrdenCompraEmitidaEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow,
            CorrelationId: Guid.NewGuid(),
            OrdenCompraId: Guid.NewGuid(),
            LicitacionId: Guid.NewGuid(),
            RequisicionId: Guid.NewGuid(),
            ObraId: Guid.NewGuid(),
            ProveedorId: Guid.NewGuid(),
            MontoTotal: 1000m,
            Items: new List<OrdenCompraEmitidaItemEvent>
            {
                new(articuloDuplicadoId, 10m, "UN", 50m, "Item 1"),
                new(articuloDuplicadoId, 5m, "UN", 100m, "Item 2")
            });

        var context = CreateConsumeContext(evento);

        // Act
        await consumer.Consume(context);

        // Assert
        Assert.Equal(0, await db.OrdenesCompra.CountAsync());
        Assert.Equal(0, await db.EventosProcesados.CountAsync());
    }

    [Fact]
    public async Task CasoE_OcExistente_MismoContenido_RegistraSoloEventoProcesadoYNoModificaOc()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        var repo = new OrdenCompraRepository(db);
        var logger = new TestLogger<OrdenCompraEmitidaConsumer>();
        var consumer = new OrdenCompraEmitidaConsumer(db, repo, logger);

        var ocId = Guid.NewGuid();
        var obraId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();

        // 1. Arrange: Se crea la OC existente respetando el dominio y se persiste
        var itemsIniciales = new[]
        {
            OrdenCompraItem.Crear(articuloId, 10m, "KG", 50m, "Cemento Inicial")
        };
        var ocInicial = OrdenCompra.Crear(ocId, obraId, proveedorId, 500m, itemsIniciales);

        db.OrdenesCompra.Add(ocInicial);
        db.EventosProcesados.Add(new EventoProcesado
        {
            EventId = Guid.NewGuid(),
            TipoEvento = nameof(OrdenCompraEmitidaEvent),
            ProcesadoEn = DateTime.UtcNow.AddMinutes(-10)
        });
        await db.SaveChangesAsync();

        // Snapshot ANTES con AsNoTracking
        var ocAntes = await db.OrdenesCompra
            .Include(o => o.Items)
            .AsNoTracking()
            .FirstAsync(o => o.Id == ocId);

        var nuevoEventId = Guid.NewGuid();
        var eventoMismoContenido = new OrdenCompraEmitidaEvent(
            EventId: nuevoEventId,
            OccurredAt: DateTime.UtcNow,
            CorrelationId: Guid.NewGuid(),
            OrdenCompraId: ocId,
            LicitacionId: Guid.NewGuid(),
            RequisicionId: Guid.NewGuid(),
            ObraId: obraId,
            ProveedorId: proveedorId,
            MontoTotal: 500m,
            Items: new List<OrdenCompraEmitidaItemEvent>
            {
                new(articuloId, 10m, "KG", 50m, "Cemento Inicial")
            });

        var context = CreateConsumeContext(eventoMismoContenido);

        // Act
        await consumer.Consume(context);

        // Snapshot DESPUÉS con AsNoTracking
        var ocDespues = await db.OrdenesCompra
            .Include(o => o.Items)
            .AsNoTracking()
            .FirstAsync(o => o.Id == ocId);

        // Assert: la OC en la base no cambió
        Assert.Equal(ocAntes.MontoTotal, ocDespues.MontoTotal);
        Assert.Equal(ocAntes.Items.Count, ocDespues.Items.Count);
        var itemAntes = ocAntes.Items.Single();
        var itemDespues = ocDespues.Items.Single();
        Assert.Equal(itemAntes.Cantidad, itemDespues.Cantidad);
        Assert.Equal(itemAntes.PrecioUnitario, itemDespues.PrecioUnitario);
        Assert.Equal(itemAntes.UnidadMedida, itemDespues.UnidadMedida);

        // Se agregó únicamente el nuevo EventoProcesado (ahora hay 2)
        Assert.Equal(2, await db.EventosProcesados.CountAsync());
        Assert.True(await db.EventosProcesados.AnyAsync(e => e.EventId == nuevoEventId));

        // Sin LogWarning emitido
        Assert.DoesNotContain(logger.Logs, l => l.LogLevel == LogLevel.Warning);
    }

    [Fact]
    public async Task CasoF_OcExistente_ContenidoDistinto_RegistraEventoProcesadoYEmiteLogWarning()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        var repo = new OrdenCompraRepository(db);
        var logger = new TestLogger<OrdenCompraEmitidaConsumer>();
        var consumer = new OrdenCompraEmitidaConsumer(db, repo, logger);

        var ocId = Guid.NewGuid();
        var obraId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();

        // 1. Arrange: Se crea la OC existente con monto 500
        var itemsIniciales = new[]
        {
            OrdenCompraItem.Crear(articuloId, 10m, "KG", 50m, "Cemento")
        };
        var ocInicial = OrdenCompra.Crear(ocId, obraId, proveedorId, 500m, itemsIniciales);

        db.OrdenesCompra.Add(ocInicial);
        db.EventosProcesados.Add(new EventoProcesado
        {
            EventId = Guid.NewGuid(),
            TipoEvento = nameof(OrdenCompraEmitidaEvent),
            ProcesadoEn = DateTime.UtcNow.AddMinutes(-5)
        });
        await db.SaveChangesAsync();

        // Snapshot ANTES con AsNoTracking
        var ocAntes = await db.OrdenesCompra
            .Include(o => o.Items)
            .AsNoTracking()
            .FirstAsync(o => o.Id == ocId);

        var nuevoEventId = Guid.NewGuid();
        // Evento con MontoTotal diferente (800) y Cantidad diferente (16)
        var eventoContenidoDistinto = new OrdenCompraEmitidaEvent(
            EventId: nuevoEventId,
            OccurredAt: DateTime.UtcNow,
            CorrelationId: Guid.NewGuid(),
            OrdenCompraId: ocId,
            LicitacionId: Guid.NewGuid(),
            RequisicionId: Guid.NewGuid(),
            ObraId: obraId,
            ProveedorId: proveedorId,
            MontoTotal: 800m,
            Items: new List<OrdenCompraEmitidaItemEvent>
            {
                new(articuloId, 16m, "KG", 50m, "Cemento Modificado")
            });

        var context = CreateConsumeContext(eventoContenidoDistinto);

        // Act
        await consumer.Consume(context);

        // Snapshot DESPUÉS con AsNoTracking
        var ocDespues = await db.OrdenesCompra
            .Include(o => o.Items)
            .AsNoTracking()
            .FirstAsync(o => o.Id == ocId);

        // Assert: la OC en la base no cambió
        Assert.Equal(ocAntes.MontoTotal, ocDespues.MontoTotal);
        Assert.Equal(ocAntes.Items.Count, ocDespues.Items.Count);
        Assert.Equal(ocAntes.Items.Single().Cantidad, ocDespues.Items.Single().Cantidad);

        // El nuevo evento procesado sí se guardó
        Assert.Equal(2, await db.EventosProcesados.CountAsync());
        Assert.True(await db.EventosProcesados.AnyAsync(e => e.EventId == nuevoEventId));

        // Verificación con TestLogger del LogWarning formateado
        var warning = Assert.Single(logger.Logs, l => l.LogLevel == LogLevel.Warning);
        Assert.Contains(ocId.ToString(), warning.Message);
        Assert.Contains(nuevoEventId.ToString(), warning.Message);
        Assert.Contains("difiere", warning.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MontoTotal difiere", warning.Message);
        Assert.Contains("Cantidad difiere", warning.Message);
    }

    [Fact]
    public async Task CasoG_OcExistente_RepositorioDevuelveNullEnObtenerPorId_LanzaInvalidOperationExceptionYSinSaveChanges()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        var mockRepo = Substitute.For<IOrdenCompraRepository>();
        var logger = new TestLogger<OrdenCompraEmitidaConsumer>();
        var consumer = new OrdenCompraEmitidaConsumer(db, mockRepo, logger);

        var ocId = Guid.NewGuid();
        var evento = new OrdenCompraEmitidaEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow,
            CorrelationId: Guid.NewGuid(),
            OrdenCompraId: ocId,
            LicitacionId: Guid.NewGuid(),
            RequisicionId: Guid.NewGuid(),
            ObraId: Guid.NewGuid(),
            ProveedorId: Guid.NewGuid(),
            MontoTotal: 500m,
            Items: new List<OrdenCompraEmitidaItemEvent>
            {
                new(Guid.NewGuid(), 5m, "UN", 100m, "Articulo")
            });

        // Configuración de mockRepo: reporta ExisteAsync = true pero ObtenerPorIdAsync = null
        mockRepo.ExisteAsync(ocId, Arg.Any<CancellationToken>()).Returns(true);
        mockRepo.ObtenerPorIdAsync(ocId, Arg.Any<CancellationToken>()).Returns((OrdenCompra?)null);

        var context = CreateConsumeContext(evento);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.Consume(context));
        Assert.Contains(ocId.ToString(), ex.Message);
        Assert.Contains("ObtenerPorIdAsync devolvió null", ex.Message);

        // Verificar que NO se ejecutó SaveChangesAsync (0 eventos procesados guardados)
        Assert.Equal(0, await db.EventosProcesados.CountAsync());
    }
}

public class TestLogger<T> : ILogger<T>
{
    public List<LogEntry> Logs { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        Logs.Add(new LogEntry(logLevel, eventId, message, exception));
    }
}

public record LogEntry(LogLevel LogLevel, EventId EventId, string Message, Exception? Exception);
