using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using ObraSync.Contracts.Events;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Handlers;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Queries;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Domain.Enums;
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
    public async Task CasoH_EventoConEtapa_OcNueva_GuardaLaEtapaSinEspaciosSobrantes()
    {
        // Arrange
        using var db = CreateDbContext(Guid.NewGuid().ToString());
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
            },
            Etapa: "  Estructura ");

        // Act
        await consumer.Consume(CreateConsumeContext(evento));

        // Assert
        var oc = await db.OrdenesCompra.SingleAsync(o => o.Id == evento.OrdenCompraId);
        Assert.Equal("Estructura", oc.Etapa);
    }

    [Fact]
    public async Task CasoI_EventoSinEtapa_OcNueva_LaEtapaQuedaNula()
    {
        // Arrange
        using var db = CreateDbContext(Guid.NewGuid().ToString());
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

        // Act
        await consumer.Consume(CreateConsumeContext(evento));

        // Assert
        var oc = await db.OrdenesCompra.SingleAsync(o => o.Id == evento.OrdenCompraId);
        Assert.Null(oc.Etapa);
    }

    [Fact]
    public async Task CasoJ_OcExistente_ConEtapaDistintaEnElEvento_NoModificaLaOcYEmiteLogWarning()
    {
        // Arrange
        using var db = CreateDbContext(Guid.NewGuid().ToString());
        var repo = new OrdenCompraRepository(db);
        var logger = new TestLogger<OrdenCompraEmitidaConsumer>();
        var consumer = new OrdenCompraEmitidaConsumer(db, repo, logger);

        var ocId = Guid.NewGuid();
        var obraId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();

        var ocInicial = OrdenCompra.Crear(
            ocId, obraId, proveedorId, 500m,
            new[] { OrdenCompraItem.Crear(articuloId, 10m, "KG", 50m, "Cemento") },
            etapa: "Estructura");
        db.OrdenesCompra.Add(ocInicial);
        await db.SaveChangesAsync();

        var evento = new OrdenCompraEmitidaEvent(
            EventId: Guid.NewGuid(),
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
                new(articuloId, 10m, "KG", 50m, "Cemento")
            },
            Etapa: "Terminaciones");

        // Act
        await consumer.Consume(CreateConsumeContext(evento));

        // Assert: la OC es inmutable (la etapa original se conserva) y la diferencia queda en el log
        var oc = await db.OrdenesCompra.AsNoTracking().SingleAsync(o => o.Id == ocId);
        Assert.Equal("Estructura", oc.Etapa);
        Assert.True(await db.EventosProcesados.AnyAsync(e => e.EventId == evento.EventId));
        Assert.Contains(logger.Logs, l => l.LogLevel == LogLevel.Warning);
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

    // ── Tarjetas #95 y #154: Evaluación presupuestaria, bloqueo y auditoría ────

    [Fact]
    public async Task CasoK_OcNoSuperaPartida_QuedaActiva_IncrementaMontoComprometido_YNoPublicaAuditoria()
    {
        // Arrange
        using var db = CreateDbContext(Guid.NewGuid().ToString());
        var repo = new OrdenCompraRepository(db);
        var logger = new TestLogger<OrdenCompraEmitidaConsumer>();
        var consumer = new OrdenCompraEmitidaConsumer(db, repo, logger);

        var obraId = Guid.NewGuid();
        var partida = new PartidaPresupuestaria
        {
            Id = Guid.NewGuid(),
            ObraId = obraId,
            Etapa = "Estructura",
            CodigoPartida = "EST-01",
            Descripcion = "Hormigón y acero",
            MontoAsignado = 100_000m,
            MontoComprometido = 40_000m,
            MontoEjecutado = 0m
        };
        db.PartidasPresupuestarias.Add(partida);
        await db.SaveChangesAsync();

        var evento = new OrdenCompraEmitidaEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow,
            CorrelationId: Guid.NewGuid(),
            OrdenCompraId: Guid.NewGuid(),
            LicitacionId: Guid.NewGuid(),
            RequisicionId: Guid.NewGuid(),
            ObraId: obraId,
            ProveedorId: Guid.NewGuid(),
            MontoTotal: 60_000m, // 40k + 60k = 100k == 100k (no supera)
            Items: new List<OrdenCompraEmitidaItemEvent>
            {
                new(Guid.NewGuid(), 60m, "M3", 1000m, "Hormigón H30")
            },
            Etapa: "Estructura");

        var context = CreateConsumeContext(evento);

        // Act
        await consumer.Consume(context);

        // Assert: OC Activa y presente en órdenes pendientes
        var oc = await db.OrdenesCompra.SingleAsync(o => o.Id == evento.OrdenCompraId);
        Assert.Equal(EstadoOrdenCompra.Activa, oc.Estado);
        Assert.False(oc.EstaBloqueada);
        Assert.Null(oc.MotivoBloqueo);

        // Assert: MontoComprometido subió de 40.000 a 100.000
        var partidaActualizada = await db.PartidasPresupuestarias.SingleAsync(p => p.Id == partida.Id);
        Assert.Equal(100_000m, partidaActualizada.MontoComprometido);

        // Assert: Aparece en ordenes-pendientes
        var pendientesHandler = new ObtenerOrdenesPendientesQueryHandler(db);
        var pendientes = await pendientesHandler.Handle(new ObtenerOrdenesPendientesQuery(obraId));
        Assert.Contains(pendientes, p => p.OrdenCompraId == evento.OrdenCompraId);

        // Assert: No se publicó evento de bloqueo
        await context.DidNotReceive().Publish(Arg.Any<OrdenCompraBloqueadaEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CasoL_OcSuperaPartida_QuedaBloqueada_NoSumaMontoComprometido_NoApareceEnPendientes_YPublicaOrdenCompraBloqueadaEvent()
    {
        // Arrange
        using var db = CreateDbContext(Guid.NewGuid().ToString());
        var repo = new OrdenCompraRepository(db);
        var logger = new TestLogger<OrdenCompraEmitidaConsumer>();
        var consumer = new OrdenCompraEmitidaConsumer(db, repo, logger);

        var obraId = Guid.NewGuid();
        var partida = new PartidaPresupuestaria
        {
            Id = Guid.NewGuid(),
            ObraId = obraId,
            Etapa = "Estructura",
            CodigoPartida = "EST-01",
            Descripcion = "Hormigón y acero",
            MontoAsignado = 100_000m,
            MontoComprometido = 40_000m,
            MontoEjecutado = 0m
        };
        db.PartidasPresupuestarias.Add(partida);
        await db.SaveChangesAsync();

        var correlationId = Guid.NewGuid();
        var evento = new OrdenCompraEmitidaEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow,
            CorrelationId: correlationId,
            OrdenCompraId: Guid.NewGuid(),
            LicitacionId: Guid.NewGuid(),
            RequisicionId: Guid.NewGuid(),
            ObraId: obraId,
            ProveedorId: Guid.NewGuid(),
            MontoTotal: 75_000m, // 40k + 75k = 115k > 100k (exceso: 15k)
            Items: new List<OrdenCompraEmitidaItemEvent>
            {
                new(Guid.NewGuid(), 75m, "M3", 1000m, "Hormigón H30")
            },
            Etapa: "estructura"); // minúscula para probar coincidencia case-insensitive

        var context = CreateConsumeContext(evento);

        // Act
        await consumer.Consume(context);

        // Assert 1: OC guardada como Bloqueada con su motivo
        var oc = await db.OrdenesCompra.SingleAsync(o => o.Id == evento.OrdenCompraId);
        Assert.Equal(EstadoOrdenCompra.Bloqueada, oc.Estado);
        Assert.True(oc.EstaBloqueada);
        Assert.NotNull(oc.MotivoBloqueo);
        Assert.Contains("supera la partida asignada", oc.MotivoBloqueo);

        // Assert 2: MontoComprometido NO se incrementó (sigue en 40.000)
        var partidaActualizada = await db.PartidasPresupuestarias.SingleAsync(p => p.Id == partida.Id);
        Assert.Equal(40_000m, partidaActualizada.MontoComprometido);

        // Assert 3: NO aparece en ordenes-pendientes
        var pendientesHandler = new ObtenerOrdenesPendientesQueryHandler(db);
        var pendientes = await pendientesHandler.Handle(new ObtenerOrdenesPendientesQuery(obraId));
        Assert.DoesNotContain(pendientes, p => p.OrdenCompraId == evento.OrdenCompraId);

        // Assert 4: Se publicó exactamente 1 OrdenCompraBloqueadaEvent con los datos exactos
        await context.Received(1).Publish(
            Arg.Is<OrdenCompraBloqueadaEvent>(ev =>
                ev.EventId != Guid.Empty &&
                ev.CorrelationId == correlationId &&
                ev.OrdenCompraId == evento.OrdenCompraId &&
                ev.ObraId == obraId &&
                ev.ProveedorId == evento.ProveedorId &&
                ev.PartidaId == partida.Id &&
                ev.Etapa == "estructura" &&
                ev.MontoOrdenCompra == 75_000m &&
                ev.MontoAsignadoPartida == 100_000m &&
                ev.MontoComprometidoActual == 40_000m &&
                ev.MontoProyectado == 115_000m &&
                ev.Exceso == 15_000m &&
                !string.IsNullOrWhiteSpace(ev.Motivo)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CasoM_OcSinPartidaDefinida_QuedaActiva_YNoPublicaAuditoria()
    {
        // Arrange (#154): obra/etapa sin partida presupuestaria cargada no se bloquea preventivamente
        using var db = CreateDbContext(Guid.NewGuid().ToString());
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
            MontoTotal: 250_000m,
            Items: new List<OrdenCompraEmitidaItemEvent>
            {
                new(Guid.NewGuid(), 250m, "UN", 1000m, "Acero")
            },
            Etapa: "Terminaciones");

        var context = CreateConsumeContext(evento);

        // Act
        await consumer.Consume(context);

        // Assert
        var oc = await db.OrdenesCompra.SingleAsync(o => o.Id == evento.OrdenCompraId);
        Assert.Equal(EstadoOrdenCompra.Activa, oc.Estado);
        await context.DidNotReceive().Publish(Arg.Any<OrdenCompraBloqueadaEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CasoN_IdempotenciaConDesvio_MismoEventIdODistintoEventIdMismaOc_NoDuplicaPublicacionDeAuditoria()
    {
        // Arrange
        using var db = CreateDbContext(Guid.NewGuid().ToString());
        var repo = new OrdenCompraRepository(db);
        var logger = Substitute.For<ILogger<OrdenCompraEmitidaConsumer>>();
        var consumer = new OrdenCompraEmitidaConsumer(db, repo, logger);

        var obraId = Guid.NewGuid();
        db.PartidasPresupuestarias.Add(new PartidaPresupuestaria
        {
            Id = Guid.NewGuid(),
            ObraId = obraId,
            Etapa = null,
            CodigoPartida = "GEN-01",
            Descripcion = "Partida general de obra",
            MontoAsignado = 10_000m,
            MontoComprometido = 5_000m
        });
        await db.SaveChangesAsync();

        var ocId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();
        var items = new List<OrdenCompraEmitidaItemEvent>
        {
            new(Guid.NewGuid(), 10m, "UN", 800m, "Material")
        };

        var primerEvento = new OrdenCompraEmitidaEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow,
            CorrelationId: Guid.NewGuid(),
            OrdenCompraId: ocId,
            LicitacionId: Guid.NewGuid(),
            RequisicionId: Guid.NewGuid(),
            ObraId: obraId,
            ProveedorId: proveedorId,
            MontoTotal: 8_000m, // 5k + 8k = 13k > 10k -> Bloquea
            Items: items);

        var context = CreateConsumeContext(primerEvento);

        // Act 1: primer consumo
        await consumer.Consume(context);

        // Act 2: reintento con el MISMO EventId
        await consumer.Consume(context);

        // Act 3: reenvío con un NUEVO EventId pero el MISMO OrdenCompraId
        var segundoEventoMismaOc = primerEvento with { EventId = Guid.NewGuid() };
        var contextSegundo = CreateConsumeContext(segundoEventoMismaOc);
        await consumer.Consume(contextSegundo);

        // Assert: solo se publicó 1 vez en el primer consumo, y 0 veces en el segundo contexto
        await context.Received(1).Publish(Arg.Any<OrdenCompraBloqueadaEvent>(), Arg.Any<CancellationToken>());
        await contextSegundo.DidNotReceive().Publish(Arg.Any<OrdenCompraBloqueadaEvent>(), Arg.Any<CancellationToken>());
        Assert.Equal(1, await db.OrdenesCompra.CountAsync());
    }

    [Fact]
    public async Task CasoO_IdempotenciaSinDesvio_MismoEventIdODistintoEventIdMismaOc_NoSumaDosVecesAMontoComprometido()
    {
        // Arrange
        using var db = CreateDbContext(Guid.NewGuid().ToString());
        var repo = new OrdenCompraRepository(db);
        var logger = Substitute.For<ILogger<OrdenCompraEmitidaConsumer>>();
        var consumer = new OrdenCompraEmitidaConsumer(db, repo, logger);

        var obraId = Guid.NewGuid();
        var partidaId = Guid.NewGuid();
        db.PartidasPresupuestarias.Add(new PartidaPresupuestaria
        {
            Id = partidaId,
            ObraId = obraId,
            Etapa = "Cimientos",
            CodigoPartida = "CIM-01",
            Descripcion = "Excavación y fundaciones",
            MontoAsignado = 50_000m,
            MontoComprometido = 10_000m
        });
        await db.SaveChangesAsync();

        var ocId = Guid.NewGuid();
        var primerEvento = new OrdenCompraEmitidaEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow,
            CorrelationId: Guid.NewGuid(),
            OrdenCompraId: ocId,
            LicitacionId: Guid.NewGuid(),
            RequisicionId: Guid.NewGuid(),
            ObraId: obraId,
            ProveedorId: Guid.NewGuid(),
            MontoTotal: 15_000m, // 10k + 15k = 25k <= 50k
            Items: new List<OrdenCompraEmitidaItemEvent>
            {
                new(Guid.NewGuid(), 15m, "M3", 1000m, "Tierra")
            },
            Etapa: "Cimientos");

        // Act 1: primer consumo -> MontoComprometido sube a 25.000
        await consumer.Consume(CreateConsumeContext(primerEvento));

        // Act 2: mismo EventId repetido
        await consumer.Consume(CreateConsumeContext(primerEvento));

        // Act 3: distinto EventId pero mismo OrdenCompraId
        var segundoEventoMismaOc = primerEvento with { EventId = Guid.NewGuid() };
        await consumer.Consume(CreateConsumeContext(segundoEventoMismaOc));

        // Assert: MontoComprometido se incrementó una sola vez (25.000, no 40.000 ni 55.000)
        var partida = await db.PartidasPresupuestarias.SingleAsync(p => p.Id == partidaId);
        Assert.Equal(25_000m, partida.MontoComprometido);
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
