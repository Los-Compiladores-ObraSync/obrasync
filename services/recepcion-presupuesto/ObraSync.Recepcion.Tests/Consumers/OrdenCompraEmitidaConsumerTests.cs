using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using ObraSync.Contracts.Events;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Handlers;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Queries;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Domain.Enums;
using ObraSync.Recepcion.Infrastructure.Configuration;
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

    // ── Tarjetas #196, #95, #154 y #195: Emparejamiento de etapa, evaluación presupuestaria, bloqueo, auditoría y concurrencia ────

    [Fact]
    public async Task CasoK_EventoConEtapaQueCoincideConPartida_EmparejaYComprometePresupuesto_SinPartidaFalso()
    {
        // Arrange (#196): Etapa con espacios y tildes empareja con la partida y compromete presupuesto
        using var db = CreateDbContext(Guid.NewGuid().ToString());
        var repo = new OrdenCompraRepository(db);
        var logger = Substitute.For<ILogger<OrdenCompraEmitidaConsumer>>();
        var consumer = new OrdenCompraEmitidaConsumer(db, repo, logger);

        var obraId = Guid.NewGuid();
        var partida = new PartidaPresupuestaria
        {
            Id = Guid.NewGuid(),
            ObraId = obraId,
            Etapa = "Fundación",
            CodigoPartida = "PART-FUND",
            Descripcion = "Fundaciones",
            MontoAsignado = 50_000m,
            MontoComprometido = 10_000m,
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
            MontoTotal: 15_000m,
            Items: new List<OrdenCompraEmitidaItemEvent>
            {
                new(Guid.NewGuid(), 10m, "UN", 1500m, "Hierro")
            },
            Etapa: "  fundacion "); // Sin tilde y con espacios

        // Act
        await consumer.Consume(CreateConsumeContext(evento));

        // Assert
        var oc = await db.OrdenesCompra.SingleAsync(o => o.Id == evento.OrdenCompraId);
        Assert.Equal(EstadoOrdenCompra.Activa, oc.Estado);
        Assert.False(oc.SinPartidaPresupuestaria);

        var partidaActualizada = await db.PartidasPresupuestarias.SingleAsync(p => p.Id == partida.Id);
        Assert.Equal(25_000m, partidaActualizada.MontoComprometido); // 10k + 15k
    }

    [Fact]
    public async Task CasoL_EventoConEtapaInexistentePeroConPartidaGeneralDeObra_UsaRespaldoDeObraYCompromete()
    {
        // Arrange (#196): Si no hay partida para la etapa, usa como respaldo la partida general de obra (Etapa == null)
        using var db = CreateDbContext(Guid.NewGuid().ToString());
        var repo = new OrdenCompraRepository(db);
        var logger = Substitute.For<ILogger<OrdenCompraEmitidaConsumer>>();
        var consumer = new OrdenCompraEmitidaConsumer(db, repo, logger);

        var obraId = Guid.NewGuid();
        var partidaGeneral = new PartidaPresupuestaria
        {
            Id = Guid.NewGuid(),
            ObraId = obraId,
            Etapa = null, // Partida de toda la obra
            CodigoPartida = "PART-OBRA-GENERAL",
            Descripcion = "Presupuesto General de Obra",
            MontoAsignado = 100_000m,
            MontoComprometido = 0m,
            MontoEjecutado = 0m
        };
        db.PartidasPresupuestarias.Add(partidaGeneral);
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
            MontoTotal: 30_000m,
            Items: new List<OrdenCompraEmitidaItemEvent>
            {
                new(Guid.NewGuid(), 10m, "UN", 3000m, "Cañería")
            },
            Etapa: "Instalación Eléctrica"); // Etapa sin partida propia

        // Act
        await consumer.Consume(CreateConsumeContext(evento));

        // Assert
        var oc = await db.OrdenesCompra.SingleAsync(o => o.Id == evento.OrdenCompraId);
        Assert.Equal(EstadoOrdenCompra.Activa, oc.Estado);
        Assert.False(oc.SinPartidaPresupuestaria);

        var partidaActualizada = await db.PartidasPresupuestarias.SingleAsync(p => p.Id == partidaGeneral.Id);
        Assert.Equal(30_000m, partidaActualizada.MontoComprometido);
    }

    private static OrdenCompraEmitidaEvent EventoConEtapa(Guid obraId, string etapa, decimal montoTotal) => new(
        EventId: Guid.NewGuid(),
        OccurredAt: DateTime.UtcNow,
        CorrelationId: Guid.NewGuid(),
        OrdenCompraId: Guid.NewGuid(),
        LicitacionId: Guid.NewGuid(),
        RequisicionId: Guid.NewGuid(),
        ObraId: obraId,
        ProveedorId: Guid.NewGuid(),
        MontoTotal: montoTotal,
        Items: new List<OrdenCompraEmitidaItemEvent>
        {
            new(Guid.NewGuid(), 1m, "UN", montoTotal, "Insumo")
        },
        Etapa: etapa);

    [Fact]
    public async Task CasoQ_ConMontoSemilla_ObraSinPartida_CreaPartidaSemillaYComprometeLaOrden()
    {
        // Presupuesto semilla: la primera orden de una obra y etapa sin partida crea la partida y se evalúa contra ella.
        using var db = CreateDbContext(Guid.NewGuid().ToString());
        var repo = new OrdenCompraRepository(db);
        var logger = new TestLogger<OrdenCompraEmitidaConsumer>();
        var opciones = Options.Create(new PresupuestoSemillaOptions { MontoSemillaPorEtapa = 100_000m });
        var consumer = new OrdenCompraEmitidaConsumer(db, repo, logger, opciones);

        var obraId = Guid.NewGuid();
        var evento = EventoConEtapa(obraId, "Pintura", 5_000m);
        var context = CreateConsumeContext(evento);

        await consumer.Consume(context);

        var partida = await db.PartidasPresupuestarias.SingleAsync(p => p.ObraId == obraId);
        Assert.Equal("Pintura", partida.Etapa);
        Assert.StartsWith("SEMILLA-", partida.CodigoPartida);
        Assert.Equal(100_000m, partida.MontoAsignado);
        Assert.Equal(5_000m, partida.MontoComprometido);

        var oc = await db.OrdenesCompra.SingleAsync(o => o.Id == evento.OrdenCompraId);
        Assert.Equal(EstadoOrdenCompra.Activa, oc.Estado);
        Assert.False(oc.SinPartidaPresupuestaria);
    }

    [Fact]
    public async Task CasoR_ConMontoSemilla_OrdenQueSuperaLaSemilla_QuedaBloqueada()
    {
        using var db = CreateDbContext(Guid.NewGuid().ToString());
        var repo = new OrdenCompraRepository(db);
        var logger = new TestLogger<OrdenCompraEmitidaConsumer>();
        var opciones = Options.Create(new PresupuestoSemillaOptions { MontoSemillaPorEtapa = 1_000m });
        var consumer = new OrdenCompraEmitidaConsumer(db, repo, logger, opciones);

        var obraId = Guid.NewGuid();
        var evento = EventoConEtapa(obraId, "Pintura", 5_000m);
        var context = CreateConsumeContext(evento);

        await consumer.Consume(context);

        var oc = await db.OrdenesCompra.SingleAsync(o => o.Id == evento.OrdenCompraId);
        Assert.Equal(EstadoOrdenCompra.Bloqueada, oc.Estado);
        Assert.False(oc.SinPartidaPresupuestaria);
        await context.Received(1).Publish(Arg.Any<OrdenCompraBloqueadaEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CasoS_ConMontoSemilla_ObraConPartidaPropia_NoCreaOtraPartida()
    {
        using var db = CreateDbContext(Guid.NewGuid().ToString());
        var repo = new OrdenCompraRepository(db);
        var logger = new TestLogger<OrdenCompraEmitidaConsumer>();
        var opciones = Options.Create(new PresupuestoSemillaOptions { MontoSemillaPorEtapa = 100_000m });
        var consumer = new OrdenCompraEmitidaConsumer(db, repo, logger, opciones);

        var obraId = Guid.NewGuid();
        db.PartidasPresupuestarias.Add(new PartidaPresupuestaria
        {
            ObraId = obraId,
            Etapa = "Pintura",
            CodigoPartida = "P-001",
            Descripcion = "Partida cargada por Compras",
            MontoAsignado = 50_000m
        });
        await db.SaveChangesAsync();

        await consumer.Consume(CreateConsumeContext(EventoConEtapa(obraId, "Pintura", 5_000m)));

        var partida = await db.PartidasPresupuestarias.SingleAsync(p => p.ObraId == obraId);
        Assert.Equal("P-001", partida.CodigoPartida);
        Assert.Equal(5_000m, partida.MontoComprometido);
    }

    [Fact]
    public async Task CasoM_EventoConEtapaSinPartidaNiRespaldo_NoSeBloquea_QuedaMarcadaSinPartidaPresupuestaria()
    {
        // Arrange (#196, #154): Si no hay partida ni de etapa ni general de obra, NO se bloquea, pero se marca SinPartidaPresupuestaria
        using var db = CreateDbContext(Guid.NewGuid().ToString());
        var repo = new OrdenCompraRepository(db);
        var logger = new TestLogger<OrdenCompraEmitidaConsumer>();
        var consumer = new OrdenCompraEmitidaConsumer(db, repo, logger);

        var obraId = Guid.NewGuid();
        // Base de datos sin partidas para esta obra

        var evento = new OrdenCompraEmitidaEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow,
            CorrelationId: Guid.NewGuid(),
            OrdenCompraId: Guid.NewGuid(),
            LicitacionId: Guid.NewGuid(),
            RequisicionId: Guid.NewGuid(),
            ObraId: obraId,
            ProveedorId: Guid.NewGuid(),
            MontoTotal: 5000m,
            Items: new List<OrdenCompraEmitidaItemEvent>
            {
                new(Guid.NewGuid(), 5m, "UN", 1000m, "Pintura")
            },
            Etapa: "Pintura");

        var context = CreateConsumeContext(evento);

        // Act
        await consumer.Consume(context);

        // Assert
        var oc = await db.OrdenesCompra.SingleAsync(o => o.Id == evento.OrdenCompraId);
        Assert.Equal(EstadoOrdenCompra.Activa, oc.Estado); // No se bloquea
        Assert.True(oc.SinPartidaPresupuestaria); // Queda marcada
        await context.DidNotReceive().Publish(Arg.Any<OrdenCompraBloqueadaEvent>(), Arg.Any<CancellationToken>());

        Assert.Contains(logger.Logs, l =>
            l.LogLevel == LogLevel.Warning &&
            l.Message.Contains("no encontró partida presupuestaria", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CasoN_EventoConEtapaQueSuperaPartida_SeBloqueaPorDesvioPresupuestario_YNoEsSinPartida()
    {
        // Arrange (#94, #95, #196): Al superar la partida, la OC se bloquea preventivamente y no compromete monto
        using var db = CreateDbContext(Guid.NewGuid().ToString());
        var repo = new OrdenCompraRepository(db);
        var logger = new TestLogger<OrdenCompraEmitidaConsumer>();
        var consumer = new OrdenCompraEmitidaConsumer(db, repo, logger);

        var obraId = Guid.NewGuid();
        var partida = new PartidaPresupuestaria
        {
            Id = Guid.NewGuid(),
            ObraId = obraId,
            Etapa = "Cimientos",
            CodigoPartida = "PART-CIM-MAX",
            Descripcion = "Cimientos",
            MontoAsignado = 10_000m,
            MontoComprometido = 0m,
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
            MontoTotal: 15_000m, // Supera los 10k asignados
            Items: new List<OrdenCompraEmitidaItemEvent>
            {
                new(Guid.NewGuid(), 1m, "UN", 15_000m, "Excavación pesada")
            },
            Etapa: "  CIMIENTOS ");

        var context = CreateConsumeContext(evento);

        // Act
        await consumer.Consume(context);

        // Assert
        var oc = await db.OrdenesCompra.SingleAsync(o => o.Id == evento.OrdenCompraId);
        Assert.Equal(EstadoOrdenCompra.Bloqueada, oc.Estado);
        Assert.True(oc.EstaBloqueada);
        Assert.False(oc.SinPartidaPresupuestaria);
        Assert.Contains("supera la partida asignada", oc.MotivoBloqueo, StringComparison.OrdinalIgnoreCase);

        var partidaActualizada = await db.PartidasPresupuestarias.SingleAsync(p => p.Id == partida.Id);
        Assert.Equal(0m, partidaActualizada.MontoComprometido); // NO se compromete al estar bloqueada
        await context.Received(1).Publish(Arg.Any<OrdenCompraBloqueadaEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CasoO_OcNoSuperaPartida_QuedaActiva_IncrementaMontoComprometido_YNoPublicaAuditoria()
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

        // Assert: OC Activa sin despachar NO aparece en órdenes pendientes hasta recibir OrdenCompraDespachadaEvent (#204)
        var oc = await db.OrdenesCompra.SingleAsync(o => o.Id == evento.OrdenCompraId);
        Assert.Equal(EstadoOrdenCompra.Activa, oc.Estado);
        Assert.False(oc.EstaBloqueada);
        Assert.False(oc.EstaDespachada);
        Assert.Null(oc.MotivoBloqueo);

        // Assert: MontoComprometido subió de 40.000 a 100.000
        var partidaActualizada = await db.PartidasPresupuestarias.SingleAsync(p => p.Id == partida.Id);
        Assert.Equal(100_000m, partidaActualizada.MontoComprometido);

        // Assert: Sin despachar todavía NO aparece en ordenes-pendientes
        var pendientesHandler = new ObtenerOrdenesPendientesQueryHandler(db);
        var pendientesAntes = await pendientesHandler.Handle(new ObtenerOrdenesPendientesQuery(obraId));
        Assert.DoesNotContain(pendientesAntes, p => p.OrdenCompraId == evento.OrdenCompraId);

        // Assert: Tras consumir OrdenCompraDespachadaEvent, SÍ aparece en ordenes-pendientes
        var dispatchConsumer = new OrdenCompraDespachadaConsumer(
            db, repo, new TestLogger<OrdenCompraDespachadaConsumer>());
        var dispatchContext = Substitute.For<ConsumeContext<OrdenCompraDespachadaEvent>>();
        dispatchContext.Message.Returns(new OrdenCompraDespachadaEvent(
            Guid.NewGuid(), DateTime.UtcNow, evento.CorrelationId, evento.OrdenCompraId, obraId, DateTime.UtcNow));
        await dispatchConsumer.Consume(dispatchContext);

        var pendientes = await pendientesHandler.Handle(new ObtenerOrdenesPendientesQuery(obraId));
        Assert.Contains(pendientes, p => p.OrdenCompraId == evento.OrdenCompraId);

        // Assert: No se publicó evento de bloqueo
        await context.DidNotReceive().Publish(Arg.Any<OrdenCompraBloqueadaEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CasoP_OcSuperaPartida_QuedaBloqueada_NoSumaMontoComprometido_NoApareceEnPendientes_YPublicaOrdenCompraBloqueadaEvent()
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
    public async Task CasoQ_IdempotenciaConDesvio_MismoEventIdODistintoEventIdMismaOc_NoDuplicaPublicacionDeAuditoria()
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
    public async Task CasoR_IdempotenciaSinDesvio_MismoEventIdODistintoEventIdMismaOc_NoSumaDosVecesAMontoComprometido()
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

    [Fact]
    public async Task CasoS_DosOrdenesSimultaneasMismaPartida_PrimeraCompromete_SegundaLanzaConcurrenciaYEnReintentoQuedaBloqueada()
    {
        // Arrange (#195): Partida con $100.000 asignados y $0 comprometidos.
        // Dos órdenes de $60.000 compiten al mismo tiempo: individualmente caben ($60k <= $100k),
        // pero juntas superan la partida ($120k > $100k).
        var dbName = Guid.NewGuid().ToString();
        var obraId = Guid.NewGuid();
        var partidaId = Guid.NewGuid();

        using (var dbSetup = CreateDbContext(dbName))
        {
            dbSetup.PartidasPresupuestarias.Add(new PartidaPresupuestaria
            {
                Id = partidaId,
                ObraId = obraId,
                Etapa = "Estructura",
                CodigoPartida = "EST-01",
                Descripcion = "Hormigón",
                MontoAsignado = 100_000m,
                MontoComprometido = 0m
            });
            await dbSetup.SaveChangesAsync();
        }

        var eventoOc1 = new OrdenCompraEmitidaEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow,
            CorrelationId: Guid.NewGuid(),
            OrdenCompraId: Guid.NewGuid(),
            LicitacionId: Guid.NewGuid(),
            RequisicionId: Guid.NewGuid(),
            ObraId: obraId,
            ProveedorId: Guid.NewGuid(),
            MontoTotal: 60_000m,
            Items: new List<OrdenCompraEmitidaItemEvent>
            {
                new(Guid.NewGuid(), 60m, "M3", 1000m, "Hormigón OC 1")
            },
            Etapa: "Estructura");

        var eventoOc2 = new OrdenCompraEmitidaEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow,
            CorrelationId: Guid.NewGuid(),
            OrdenCompraId: Guid.NewGuid(),
            LicitacionId: Guid.NewGuid(),
            RequisicionId: Guid.NewGuid(),
            ObraId: obraId,
            ProveedorId: Guid.NewGuid(),
            MontoTotal: 60_000m,
            Items: new List<OrdenCompraEmitidaItemEvent>
            {
                new(Guid.NewGuid(), 60m, "M3", 1000m, "Hormigón OC 2")
            },
            Etapa: "Estructura");

        using var db1 = CreateDbContext(dbName);
        var repo1 = new OrdenCompraRepository(db1);
        var logger1 = new TestLogger<OrdenCompraEmitidaConsumer>();
        var consumer1 = new OrdenCompraEmitidaConsumer(db1, repo1, logger1);
        var context1 = CreateConsumeContext(eventoOc1);

        var optionsDb2 = new DbContextOptionsBuilder<RecepcionDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        // Justo después de que el consumidor 2 leyó MontoComprometido = 0 y evaluó que cabía,
        // antes de confirmar su SaveChangesAsync, el consumidor 1 confirma su transacción y avanza el RowVersion.
        using var db2 = new ConcurrenciaIntercaladaDbContext(
            optionsDb2,
            antesDeGuardarAsync: () => consumer1.Consume(context1));

        var repo2 = new OrdenCompraRepository(db2);
        var logger2 = new TestLogger<OrdenCompraEmitidaConsumer>();
        var consumer2 = new OrdenCompraEmitidaConsumer(db2, repo2, logger2);
        var context2Intento1 = CreateConsumeContext(eventoOc2);

        // Act 1: El primer intento de la OC 2 detecta el cambio de RowVersion y lanza DbUpdateConcurrencyException
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => consumer2.Consume(context2Intento1));
        Assert.Contains(logger2.Logs, l => l.LogLevel == LogLevel.Warning && l.Message.Contains("Conflicto de concurrencia"));

        // Act 2: MassTransit reintenta el mensaje de la OC 2 en un DbContext limpio
        using var db3 = CreateDbContext(dbName);
        var repo3 = new OrdenCompraRepository(db3);
        var logger3 = new TestLogger<OrdenCompraEmitidaConsumer>();
        var consumer3 = new OrdenCompraEmitidaConsumer(db3, repo3, logger3);
        var context2Reintento = CreateConsumeContext(eventoOc2);

        await consumer3.Consume(context2Reintento);

        // Assert:
        // 1. La OC 1 quedó Activa y comprometió $60.000
        using var dbVerif = CreateDbContext(dbName);
        var oc1EnBd = await dbVerif.OrdenesCompra.SingleAsync(o => o.Id == eventoOc1.OrdenCompraId);
        Assert.Equal(EstadoOrdenCompra.Activa, oc1EnBd.Estado);

        // 2. La OC 2 quedó Bloqueada en el reintento y NO sumó al MontoComprometido
        var oc2EnBd = await dbVerif.OrdenesCompra.SingleAsync(o => o.Id == eventoOc2.OrdenCompraId);
        Assert.Equal(EstadoOrdenCompra.Bloqueada, oc2EnBd.Estado);

        var partidaFinal = await dbVerif.PartidasPresupuestarias.SingleAsync(p => p.Id == partidaId);
        Assert.Equal(60_000m, partidaFinal.MontoComprometido);

        // 3. Se publicó el evento de auditoría OrdenCompraBloqueadaEvent para la OC 2
        await context2Reintento.Received(1).Publish(
            Arg.Is<OrdenCompraBloqueadaEvent>(e =>
                e.OrdenCompraId == eventoOc2.OrdenCompraId &&
                e.MontoComprometidoActual == 60_000m &&
                e.MontoProyectado == 120_000m &&
                e.Exceso == 20_000m),
            Arg.Any<CancellationToken>());
    }

    private static OrdenCompraEmitidaEvent EventoParaIndiceUnico() => new(
        EventId: Guid.NewGuid(),
        OccurredAt: DateTime.UtcNow,
        CorrelationId: Guid.NewGuid(),
        OrdenCompraId: Guid.NewGuid(),
        LicitacionId: Guid.NewGuid(),
        RequisicionId: Guid.NewGuid(),
        ObraId: Guid.NewGuid(),
        ProveedorId: Guid.NewGuid(),
        MontoTotal: 1_000m,
        Items: new List<OrdenCompraEmitidaItemEvent> { new(Guid.NewGuid(), 1m, "UN", 1_000m, "Ítem") },
        Etapa: "Estructura");

    [Fact]
    public async Task IndiceUnico_SinQueLaOcSeHayaGuardado_RelanzaParaQueMassTransitReintente()
    {
        // Caso: dos OC de la misma obra y etapa crean la partida semilla a la vez; la segunda choca con el índice
        // único y NO se guardó. Descartarla en silencio la perdería.
        var dbName = Guid.NewGuid().ToString();
        var evento = EventoParaIndiceUnico();
        using var db = new ChoqueDeIndiceDbContext(
            new DbContextOptionsBuilder<RecepcionDbContext>().UseInMemoryDatabase(dbName).Options,
            antesDeFallarAsync: () => Task.CompletedTask);
        var consumer = new ConsumidorConIndiceUnicoSimulado(db, new OrdenCompraRepository(db),
            Substitute.For<ILogger<OrdenCompraEmitidaConsumer>>());

        await Assert.ThrowsAsync<DbUpdateException>(() => consumer.Consume(CreateConsumeContext(evento)));

        using var verif = CreateDbContext(dbName);
        Assert.Empty(await verif.OrdenesCompra.ToListAsync());
    }

    [Fact]
    public async Task IndiceUnico_CuandoLaOcYaFueProcesada_SeDescartaSinError()
    {
        // Caso: el mismo mensaje llega dos veces a la vez; el otro consumidor ya guardó la OC.
        var dbName = Guid.NewGuid().ToString();
        var evento = EventoParaIndiceUnico();

        using var dbGanador = CreateDbContext(dbName);
        var ganador = new OrdenCompraEmitidaConsumer(dbGanador, new OrdenCompraRepository(dbGanador),
            Substitute.For<ILogger<OrdenCompraEmitidaConsumer>>());

        using var db = new ChoqueDeIndiceDbContext(
            new DbContextOptionsBuilder<RecepcionDbContext>().UseInMemoryDatabase(dbName).Options,
            antesDeFallarAsync: () => ganador.Consume(CreateConsumeContext(evento)));
        var consumer = new ConsumidorConIndiceUnicoSimulado(db, new OrdenCompraRepository(db),
            Substitute.For<ILogger<OrdenCompraEmitidaConsumer>>());

        await consumer.Consume(CreateConsumeContext(evento));

        using var verif = CreateDbContext(dbName);
        Assert.Single(await verif.OrdenesCompra.ToListAsync());
    }

    private sealed class ConsumidorConIndiceUnicoSimulado : OrdenCompraEmitidaConsumer
    {
        public ConsumidorConIndiceUnicoSimulado(
            RecepcionDbContext db,
            IOrdenCompraRepository repo,
            ILogger<OrdenCompraEmitidaConsumer> logger)
            : base(db, repo, logger)
        {
        }

        protected override bool EsViolacionDeIndiceUnico(DbUpdateException ex) => true;
    }

    /// <summary>Simula que, al guardar, otro consumidor se adelantó y la base rechaza por índice único.</summary>
    private sealed class ChoqueDeIndiceDbContext : RecepcionDbContext
    {
        private readonly Func<Task> _antesDeFallarAsync;

        public ChoqueDeIndiceDbContext(DbContextOptions<RecepcionDbContext> options, Func<Task> antesDeFallarAsync)
            : base(options)
        {
            _antesDeFallarAsync = antesDeFallarAsync;
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            await _antesDeFallarAsync();
            throw new DbUpdateException("Índice único simulado");
        }
    }

    private sealed class ConcurrenciaIntercaladaDbContext : RecepcionDbContext
    {
        private readonly Func<Task> _antesDeGuardarAsync;
        private bool _intercaladoEjecutado;

        public ConcurrenciaIntercaladaDbContext(
            DbContextOptions<RecepcionDbContext> options,
            Func<Task> antesDeGuardarAsync)
            : base(options)
        {
            _antesDeGuardarAsync = antesDeGuardarAsync;
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (!_intercaladoEjecutado)
            {
                _intercaladoEjecutado = true;
                await _antesDeGuardarAsync();
            }

            return await base.SaveChangesAsync(cancellationToken);
        }
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
