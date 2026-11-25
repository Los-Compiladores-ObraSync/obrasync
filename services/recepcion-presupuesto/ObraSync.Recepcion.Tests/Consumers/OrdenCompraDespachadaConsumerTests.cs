using System.Collections.Concurrent;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using ObraSync.Contracts.Events;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Commands;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Handlers;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Queries;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Domain.Enums;
using ObraSync.Recepcion.Infrastructure.Consumers;
using ObraSync.Recepcion.Infrastructure.Messaging;
using ObraSync.Recepcion.Infrastructure.Persistence;
using ObraSync.Recepcion.Infrastructure.Persistence.Repositories;
using Xunit;

namespace ObraSync.Recepcion.Tests.Consumers;

public class OrdenCompraDespachadaConsumerTests
{
    private static RecepcionDbContext CreateDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<RecepcionDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new RecepcionDbContext(options);
    }

    private static ConsumeContext<OrdenCompraDespachadaEvent> CreateConsumeContext(
        OrdenCompraDespachadaEvent message,
        CancellationToken cancellationToken = default)
    {
        var context = Substitute.For<ConsumeContext<OrdenCompraDespachadaEvent>>();
        context.Message.Returns(message);
        context.CancellationToken.Returns(cancellationToken);
        return context;
    }

    private static OrdenCompra CrearOrden(
        Guid? ordenId = null,
        Guid? obraId = null,
        EstadoOrdenCompra estado = EstadoOrdenCompra.Activa,
        DateTime? fechaDespacho = null)
    {
        return OrdenCompra.Crear(
            id: ordenId ?? Guid.NewGuid(),
            obraId: obraId ?? Guid.NewGuid(),
            proveedorId: Guid.NewGuid(),
            montoTotal: 15_000m,
            items: new[]
            {
                OrdenCompraItem.Crear(Guid.NewGuid(), 10m, "BOLSA", 1500m, "Cemento")
            },
            etapa: "Estructura",
            estado: estado,
            motivoBloqueo: estado == EstadoOrdenCompra.Bloqueada ? "Desvío presupuestario (#95)" : null,
            fechaDespacho: fechaDespacho);
    }

    private static OrdenCompraDespachadaEvent CrearEvento(
        Guid ordenCompraId,
        Guid obraId,
        Guid? eventId = null,
        DateTime? fechaDespacho = null,
        Guid? correlationId = null)
    {
        var fecha = fechaDespacho ?? new DateTime(2026, 11, 16, 19, 0, 0, DateTimeKind.Utc);
        return new OrdenCompraDespachadaEvent(
            EventId: eventId ?? Guid.NewGuid(),
            OccurredAt: fecha,
            CorrelationId: correlationId ?? Guid.NewGuid(),
            OrdenCompraId: ordenCompraId,
            ObraId: obraId,
            FechaDespacho: fecha);
    }

    [Fact]
    public async Task EventoValido_OcActiva_MarcaFechaDespachoYRegistraEventoProcesado()
    {
        await using var db = CreateDbContext($"Desp_Activa_{Guid.NewGuid()}");
        var oc = CrearOrden();
        db.OrdenesCompra.Add(oc);
        await db.SaveChangesAsync();

        var repo = new OrdenCompraRepository(db);
        var consumer = new OrdenCompraDespachadaConsumer(db, repo, Substitute.For<ILogger<OrdenCompraDespachadaConsumer>>());

        var fechaDespacho = new DateTime(2026, 11, 16, 19, 15, 0, DateTimeKind.Utc);
        var evento = CrearEvento(oc.Id, oc.ObraId, fechaDespacho: fechaDespacho);

        await consumer.Consume(CreateConsumeContext(evento));

        var ocEnBd = await db.OrdenesCompra.AsNoTracking().SingleAsync(o => o.Id == oc.Id);
        Assert.Equal(EstadoOrdenCompra.Activa, ocEnBd.Estado);
        Assert.True(ocEnBd.EstaDespachada);
        Assert.Equal(fechaDespacho, ocEnBd.FechaDespacho);

        var procesado = await db.EventosProcesados.SingleOrDefaultAsync(e => e.EventId == evento.EventId);
        Assert.NotNull(procesado);
        Assert.Equal(nameof(OrdenCompraDespachadaEvent), procesado!.TipoEvento);
    }

    [Fact]
    public async Task Idempotencia_MismoEventIdDosVeces_NoFallaNiAlteraFechaDespacho()
    {
        await using var db = CreateDbContext($"Desp_Idemp_{Guid.NewGuid()}");
        var oc = CrearOrden();
        db.OrdenesCompra.Add(oc);
        await db.SaveChangesAsync();

        var repo = new OrdenCompraRepository(db);
        var consumer = new OrdenCompraDespachadaConsumer(db, repo, Substitute.For<ILogger<OrdenCompraDespachadaConsumer>>());

        var eventId = Guid.NewGuid();
        var fechaOriginal = new DateTime(2026, 11, 16, 18, 0, 0, DateTimeKind.Utc);
        var evento1 = CrearEvento(oc.Id, oc.ObraId, eventId: eventId, fechaDespacho: fechaOriginal);
        var evento2 = CrearEvento(oc.Id, oc.ObraId, eventId: eventId, fechaDespacho: fechaOriginal.AddHours(3));

        await consumer.Consume(CreateConsumeContext(evento1));
        await consumer.Consume(CreateConsumeContext(evento2));

        var ocEnBd = await db.OrdenesCompra.AsNoTracking().SingleAsync(o => o.Id == oc.Id);
        Assert.Equal(fechaOriginal, ocEnBd.FechaDespacho);
        Assert.Equal(1, await db.EventosProcesados.CountAsync(e => e.EventId == eventId));
    }

    [Fact]
    public async Task OrdenInexistente_LanzaExcepcionParaReintentoYNoRegistraEventoProcesado()
    {
        await using var db = CreateDbContext($"Desp_NoExiste_{Guid.NewGuid()}");
        var repo = new OrdenCompraRepository(db);
        var consumer = new OrdenCompraDespachadaConsumer(db, repo, Substitute.For<ILogger<OrdenCompraDespachadaConsumer>>());

        var evento = CrearEvento(Guid.NewGuid(), Guid.NewGuid());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            consumer.Consume(CreateConsumeContext(evento)));

        Assert.False(await db.EventosProcesados.AnyAsync(e => e.EventId == evento.EventId));
    }

    [Fact]
    public async Task ReintentosMassTransit_SiLlegaAntesQueOrdenCompraEmitida_ReintentaYProcesaAlExistirLaOrden()
    {
        var dbName = $"Desp_Retry_{Guid.NewGuid()}";
        var oc = CrearOrden();
        var evento = CrearEvento(oc.Id, oc.ObraId);
        var seguimiento = new SeguimientoReintentos(dbName, oc);

        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton(seguimiento)
            .AddDbContext<RecepcionDbContext>(o => o.UseInMemoryDatabase(dbName))
            .AddScoped<IOrdenCompraRepository>(sp =>
                new RepositorioQueSiembraTrasPrimerIntento(
                    sp.GetRequiredService<RecepcionDbContext>(),
                    sp.GetRequiredService<SeguimientoReintentos>()))
            .AddMassTransitTestHarness(x =>
            {
                x.SetTestTimeouts(
                    testTimeout: TimeSpan.FromSeconds(15),
                    testInactivityTimeout: TimeSpan.FromSeconds(10));
                x.AddConsumer<OrdenCompraDespachadaConsumer>();
                x.UsingInMemory((context, cfg) =>
                {
                    cfg.UsarReintentosExponenciales(
                        reintentos: 4,
                        esperaMinima: TimeSpan.FromMilliseconds(20),
                        esperaMaxima: TimeSpan.FromMilliseconds(100),
                        incremento: TimeSpan.FromMilliseconds(20));
                    cfg.ConfigureEndpoints(context);
                });
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(evento);

        Assert.True(await harness.Consumed.Any<OrdenCompraDespachadaEvent>());
        Assert.True(seguimiento.Intentos >= 2, $"Se esperaban al menos 2 intentos (reintento), hubo {seguimiento.Intentos}.");

        using var scope = provider.CreateScope();
        var dbAssert = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();
        var ocEnBd = await dbAssert.OrdenesCompra.AsNoTracking().SingleAsync(o => o.Id == oc.Id);
        Assert.True(ocEnBd.EstaDespachada);
        Assert.Equal(evento.FechaDespacho, ocEnBd.FechaDespacho);
        Assert.True(await dbAssert.EventosProcesados.AnyAsync(e => e.EventId == evento.EventId));
    }

    [Fact]
    public async Task IndependenciaEstadoPresupuestario_OcBloqueadaDespachadaMantieneBloqueo_YAlLiberarQuedaListaParaRecibir()
    {
        await using var db = CreateDbContext($"Desp_Bloq_{Guid.NewGuid()}");
        var obraId = Guid.NewGuid();
        var partida = new PartidaPresupuestaria
        {
            Id = Guid.NewGuid(),
            ObraId = obraId,
            Etapa = "Estructura",
            CodigoPartida = "EST-204",
            Descripcion = "Estructura",
            MontoAsignado = 10_000m,
            MontoComprometido = 0m,
            MontoEjecutado = 0m,
            CreatedAt = DateTime.UtcNow
        };
        db.PartidasPresupuestarias.Add(partida);

        var oc = CrearOrden(obraId: obraId, estado: EstadoOrdenCompra.Bloqueada);
        db.OrdenesCompra.Add(oc);
        await db.SaveChangesAsync();

        var repo = new OrdenCompraRepository(db);
        var consumer = new OrdenCompraDespachadaConsumer(db, repo, Substitute.For<ILogger<OrdenCompraDespachadaConsumer>>());
        var fechaDespacho = new DateTime(2026, 11, 16, 20, 0, 0, DateTimeKind.Utc);

        await consumer.Consume(CreateConsumeContext(CrearEvento(oc.Id, obraId, fechaDespacho: fechaDespacho)));

        // 1. Marcada como despachada, pero sigue Bloqueada -> NO aparece en pendientes
        var ocBloqueadaDespachada = await db.OrdenesCompra.AsNoTracking().SingleAsync(o => o.Id == oc.Id);
        Assert.Equal(EstadoOrdenCompra.Bloqueada, ocBloqueadaDespachada.Estado);
        Assert.True(ocBloqueadaDespachada.EstaDespachada);
        Assert.Equal(fechaDespacho, ocBloqueadaDespachada.FechaDespacho);

        var pendientesHandler = new ObtenerOrdenesPendientesQueryHandler(db);
        var pendientesAntes = await pendientesHandler.Handle(new ObtenerOrdenesPendientesQuery(obraId));
        Assert.DoesNotContain(pendientesAntes.Items, p => p.OrdenCompraId == oc.Id);

        // 2. Admin libera la orden (#96) -> pasa a Activa preservando FechaDespacho y ya aparece en pendientes
        var liberarHandler = new LiberarOrdenCompraCommandHandler(db);
        await liberarHandler.Handle(new LiberarOrdenCompraCommand(oc.Id, Guid.NewGuid(), "Autorizado"));

        var ocLiberada = await db.OrdenesCompra.AsNoTracking().SingleAsync(o => o.Id == oc.Id);
        Assert.Equal(EstadoOrdenCompra.Activa, ocLiberada.Estado);
        Assert.True(ocLiberada.EstaDespachada);
        Assert.Equal(fechaDespacho, ocLiberada.FechaDespacho);

        var pendientesDespues = await pendientesHandler.Handle(new ObtenerOrdenesPendientesQuery(obraId));
        Assert.Contains(pendientesDespues.Items, p => p.OrdenCompraId == oc.Id);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task PayloadInvalido_SeDescartaSinLanzarExcepcionNiRegistrarEvento(
        bool eventIdVacio,
        bool ordenVacia,
        bool obraVacia)
    {
        await using var db = CreateDbContext($"Desp_Inv_{Guid.NewGuid()}");
        var repo = new OrdenCompraRepository(db);
        var consumer = new OrdenCompraDespachadaConsumer(db, repo, Substitute.For<ILogger<OrdenCompraDespachadaConsumer>>());

        var evento = new OrdenCompraDespachadaEvent(
            EventId: eventIdVacio ? Guid.Empty : Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow,
            CorrelationId: Guid.NewGuid(),
            OrdenCompraId: ordenVacia ? Guid.Empty : Guid.NewGuid(),
            ObraId: obraVacia ? Guid.Empty : Guid.NewGuid(),
            FechaDespacho: DateTime.UtcNow);

        await consumer.Consume(CreateConsumeContext(evento));

        Assert.Equal(0, await db.EventosProcesados.CountAsync());
    }

    [Fact]
    public async Task EventoConFechaEstimadaYObservacion_GuardaYExponeEnOrdenesPendientes_YEsIdempotente()
    {
        await using var db = CreateDbContext($"Desp_217_{Guid.NewGuid()}");
        var oc = CrearOrden();
        db.OrdenesCompra.Add(oc);
        await db.SaveChangesAsync();

        var repo = new OrdenCompraRepository(db);
        var consumer = new OrdenCompraDespachadaConsumer(db, repo, Substitute.For<ILogger<OrdenCompraDespachadaConsumer>>());

        var eventId = Guid.NewGuid();
        var fechaDespacho = new DateTime(2026, 11, 21, 14, 0, 0, DateTimeKind.Utc);
        var fechaEstimada = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
        const string observacion = "Camión con acoplado, llamar al llegar";

        var evento1 = new OrdenCompraDespachadaEvent(
            EventId: eventId,
            OccurredAt: fechaDespacho,
            CorrelationId: Guid.NewGuid(),
            OrdenCompraId: oc.Id,
            ObraId: oc.ObraId,
            FechaDespacho: fechaDespacho,
            FechaEstimadaLlegada: fechaEstimada,
            ObservacionDespacho: observacion);

        // Mismo EventId con datos modificados -> idempotencia no debe pisar los datos guardados
        var eventoDuplicado = evento1 with
        {
            FechaEstimadaLlegada = fechaEstimada.AddDays(5),
            ObservacionDespacho = "Observación distinta"
        };

        await consumer.Consume(CreateConsumeContext(evento1));
        await consumer.Consume(CreateConsumeContext(eventoDuplicado));

        var ocEnBd = await db.OrdenesCompra.AsNoTracking().SingleAsync(o => o.Id == oc.Id);
        Assert.Equal(fechaDespacho, ocEnBd.FechaDespacho);
        Assert.Equal(fechaEstimada, ocEnBd.FechaEstimadaLlegada);
        Assert.Equal(observacion, ocEnBd.ObservacionDespacho);
        Assert.Equal(1, await db.EventosProcesados.CountAsync(e => e.EventId == eventId));

        // El Capataz ve fecha estimada y observación en ordenes-pendientes
        var pendientesHandler = new ObtenerOrdenesPendientesQueryHandler(db);
        var pendientes = await pendientesHandler.Handle(new ObtenerOrdenesPendientesQuery(oc.ObraId));
        var ordenPendiente = Assert.Single(pendientes.Items);
        Assert.Equal(fechaDespacho, ordenPendiente.FechaDespacho);
        Assert.Equal(fechaEstimada, ordenPendiente.FechaEstimadaLlegada);
        Assert.Equal(observacion, ordenPendiente.ObservacionDespacho);
    }

    private sealed class SeguimientoReintentos
    {
        private int _intentos;
        private int _sembrado;
        public string DbName { get; }
        public OrdenCompra OrdenASembrar { get; }
        public int Intentos => Volatile.Read(ref _intentos);

        public SeguimientoReintentos(string dbName, OrdenCompra ordenASembrar)
        {
            DbName = dbName;
            OrdenASembrar = ordenASembrar;
        }

        public int RegistrarIntento() => Interlocked.Increment(ref _intentos);

        public async Task AsegurarOrdenSembradaDespuesDelPrimerFalloAsync()
        {
            if (Interlocked.CompareExchange(ref _sembrado, 1, 0) == 0)
            {
                var options = new DbContextOptionsBuilder<RecepcionDbContext>()
                    .UseInMemoryDatabase(DbName)
                    .Options;
                await using var db = new RecepcionDbContext(options);
                db.OrdenesCompra.Add(OrdenCompra.Crear(
                    OrdenASembrar.Id,
                    OrdenASembrar.ObraId,
                    OrdenASembrar.ProveedorId,
                    OrdenASembrar.MontoTotal,
                    OrdenASembrar.Items.Select(i => OrdenCompraItem.Crear(
                        i.ArticuloId, i.Cantidad, i.UnidadMedida, i.PrecioUnitario, i.Descripcion)),
                    etapa: OrdenASembrar.Etapa));
                await db.SaveChangesAsync();
            }
        }
    }

    private sealed class RepositorioQueSiembraTrasPrimerIntento : IOrdenCompraRepository
    {
        private readonly OrdenCompraRepository _inner;
        private readonly SeguimientoReintentos _seguimiento;

        public RepositorioQueSiembraTrasPrimerIntento(RecepcionDbContext db, SeguimientoReintentos seguimiento)
        {
            _inner = new OrdenCompraRepository(db);
            _seguimiento = seguimiento;
        }

        public Task<OrdenCompra?> ObtenerPorIdAsync(Guid ordenCompraId, CancellationToken cancellationToken = default) =>
            _inner.ObtenerPorIdAsync(ordenCompraId, cancellationToken);

        public async Task<OrdenCompra?> ObtenerParaActualizarAsync(Guid ordenCompraId, CancellationToken cancellationToken = default)
        {
            var intento = _seguimiento.RegistrarIntento();
            var encontrada = await _inner.ObtenerParaActualizarAsync(ordenCompraId, cancellationToken);
            if (encontrada is null && intento == 1)
            {
                await _seguimiento.AsegurarOrdenSembradaDespuesDelPrimerFalloAsync();
            }
            return encontrada;
        }

        public Task<OrdenCompra?> ObtenerConItemsParaActualizarAsync(Guid ordenCompraId, CancellationToken cancellationToken = default) =>
            _inner.ObtenerConItemsParaActualizarAsync(ordenCompraId, cancellationToken);

        public Task<bool> ExisteAsync(Guid ordenCompraId, CancellationToken cancellationToken = default) =>
            _inner.ExisteAsync(ordenCompraId, cancellationToken);

        public void Agregar(OrdenCompra ordenCompra) => _inner.Agregar(ordenCompra);
    }
}
