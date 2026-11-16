using MassTransit;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ObraSync.Contracts.Events;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Infrastructure.Persistence;

namespace ObraSync.Recepcion.Infrastructure.Consumers;

public class OrdenCompraDespachadaConsumer : IConsumer<OrdenCompraDespachadaEvent>
{
    private readonly RecepcionDbContext _db;
    private readonly IOrdenCompraRepository _ordenCompraRepository;
    private readonly ILogger<OrdenCompraDespachadaConsumer> _logger;

    public OrdenCompraDespachadaConsumer(
        RecepcionDbContext db,
        IOrdenCompraRepository ordenCompraRepository,
        ILogger<OrdenCompraDespachadaConsumer> logger)
    {
        _db = db;
        _ordenCompraRepository = ordenCompraRepository;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OrdenCompraDespachadaEvent> context)
    {
        var msg = context.Message;
        var correlationId = msg.CorrelationId != Guid.Empty
            ? msg.CorrelationId
            : (context.CorrelationId ?? Guid.Empty);

        using var logScope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        });

        // ── PASO 1: Validación básica del payload ───────────────────────────
        if (msg.EventId == Guid.Empty ||
            msg.OrdenCompraId == Guid.Empty ||
            msg.ObraId == Guid.Empty ||
            msg.FechaDespacho == default)
        {
            _logger.LogWarning(
                "OrdenCompraDespachadaEvent con payload inválido, se descarta. EventId: {EventId}, OrdenCompraId: {OrdenCompraId}, CorrelationId: {CorrelationId}",
                msg.EventId, msg.OrdenCompraId, correlationId);
            return;
        }

        // ── PASO 2: Idempotencia por EventId ────────────────────────────────
        if (await _db.EventosProcesados.AnyAsync(e => e.EventId == msg.EventId, context.CancellationToken))
        {
            _logger.LogInformation(
                "Evento {EventId} de despacho duplicado, se descarta. CorrelationId: {CorrelationId}",
                msg.EventId, correlationId);
            return;
        }

        // ── PASO 3: Búsqueda de la Orden de Compra con tracking ─────────────
        var ordenCompra = await _ordenCompraRepository.ObtenerParaActualizarAsync(
            msg.OrdenCompraId,
            context.CancellationToken);

        // Regla de orden de llegada (#202, #204): si todavía no se procesó OrdenCompraEmitidaEvent,
        // NO se registra en EventosProcesados ni se descarta; se lanza excepción para que MassTransit reintente.
        if (ordenCompra is null)
        {
            _logger.LogWarning(
                "OrdenCompra {OrdenCompraId} todavía no existe al consumir OrdenCompraDespachadaEvent {EventId}. Se relanza para reintento de MassTransit. CorrelationId: {CorrelationId}",
                msg.OrdenCompraId, msg.EventId, correlationId);

            throw new InvalidOperationException(
                $"La orden de compra {msg.OrdenCompraId} todavía no fue creada por OrdenCompraEmitidaEvent.");
        }

        if (ordenCompra.ObraId != msg.ObraId)
        {
            _logger.LogWarning(
                "OrdenCompra {OrdenCompraId} pertenece a Obra {ObraExistente} pero OrdenCompraDespachadaEvent {EventId} informó Obra {ObraEvento}. Se conserva Obra existente. CorrelationId: {CorrelationId}",
                ordenCompra.Id, ordenCompra.ObraId, msg.EventId, msg.ObraId, correlationId);
        }

        // ── PASO 4: Marcar despacho (independiente de Estado Activa/Bloqueada) y registrar idempotencia
        ordenCompra.MarcarComoDespachada(msg.FechaDespacho);

        _db.EventosProcesados.Add(new EventoProcesado
        {
            EventId = msg.EventId,
            TipoEvento = nameof(OrdenCompraDespachadaEvent),
            ProcesadoEn = DateTime.UtcNow
        });

        try
        {
            await _db.SaveChangesAsync(context.CancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            _logger.LogInformation(
                "Evento {EventId} de OrdenCompra {OrdenCompraId} duplicado (detectado por índice único en BD), se descarta. CorrelationId: {CorrelationId}",
                msg.EventId, msg.OrdenCompraId, correlationId);
            return;
        }

        _logger.LogInformation(
            "OrdenCompra {OrdenCompraId} marcada como despachada en {FechaDespacho} (Estado: {Estado}). EventId: {EventId}, CorrelationId: {CorrelationId}",
            ordenCompra.Id, ordenCompra.FechaDespacho, ordenCompra.Estado, msg.EventId, correlationId);
    }
}
