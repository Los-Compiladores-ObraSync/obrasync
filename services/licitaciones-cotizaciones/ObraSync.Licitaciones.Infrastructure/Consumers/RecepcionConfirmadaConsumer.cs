using MassTransit;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ObraSync.Contracts.Events;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Infrastructure.Consumers;

/// <summary>
/// Consume RecepcionConfirmadaEvent (#222) de forma idempotente (por EventId) y marca la OrdenCompra
/// como recibida completa únicamente cuando OrdenCompleta == true y la recepción no es Rechazada.
/// </summary>
public class RecepcionConfirmadaConsumer : IConsumer<RecepcionConfirmadaEvent>
{
    private readonly LicitacionesDbContext _db;
    private readonly ILogger<RecepcionConfirmadaConsumer> _logger;

    public RecepcionConfirmadaConsumer(
        LicitacionesDbContext db,
        ILogger<RecepcionConfirmadaConsumer> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Consume(ConsumeContext<RecepcionConfirmadaEvent> context)
    {
        var msg = context.Message;
        var correlationId = msg.CorrelationId != Guid.Empty
            ? msg.CorrelationId
            : (context.CorrelationId ?? Guid.Empty);

        using var logScope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        });

        if (msg.EventId == Guid.Empty || msg.OrdenCompraId == Guid.Empty)
        {
            _logger.LogWarning(
                "RecepcionConfirmadaEvent inválido (EventId u OrdenCompraId vacío), se descarta. EventId {EventId}, OrdenCompraId {OrdenCompraId}, CorrelationId {CorrelationId}",
                msg.EventId,
                msg.OrdenCompraId,
                correlationId);
            return;
        }

        if (await _db.EventosProcesados.AnyAsync(e => e.EventId == msg.EventId, context.CancellationToken))
        {
            _logger.LogInformation(
                "RecepcionConfirmadaEvent {EventId} duplicado, se descarta. CorrelationId {CorrelationId}",
                msg.EventId,
                correlationId);
            return;
        }

        var esRechazada = !string.IsNullOrWhiteSpace(msg.Estado)
            && string.Equals(msg.Estado.Trim(), "Rechazada", StringComparison.OrdinalIgnoreCase);

        if (esRechazada || !msg.OrdenCompleta)
        {
            _db.EventosProcesados.Add(new EventoProcesado
            {
                EventId = msg.EventId,
                TipoEvento = nameof(RecepcionConfirmadaEvent),
                ProcesadoEn = DateTime.UtcNow
            });

            try
            {
                await _db.SaveChangesAsync(context.CancellationToken);
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
            {
                _logger.LogInformation(
                    "RecepcionConfirmadaEvent {EventId} duplicado por índice único, se descarta. CorrelationId {CorrelationId}",
                    msg.EventId,
                    correlationId);
                return;
            }

            _logger.LogInformation(
                "RecepcionConfirmadaEvent {EventId} para OrdenCompra {OrdenCompraId} ignorado para calificación (OrdenCompleta={OrdenCompleta}, Estado={Estado}). CorrelationId {CorrelationId}",
                msg.EventId,
                msg.OrdenCompraId,
                msg.OrdenCompleta,
                msg.Estado ?? "(no informado)",
                correlationId);
            return;
        }

        var ordenCompra = await _db.OrdenesCompra
            .FirstOrDefaultAsync(o => o.OrdenCompraId == msg.OrdenCompraId, context.CancellationToken);

        if (ordenCompra is null)
        {
            _logger.LogWarning(
                "No se encontró la OrdenCompra {OrdenCompraId} al consumir RecepcionConfirmadaEvent {EventId}. Se ignora sin error. CorrelationId {CorrelationId}",
                msg.OrdenCompraId,
                msg.EventId,
                correlationId);
            return;
        }

        var fechaRecepcion = msg.FechaRecepcion != default ? msg.FechaRecepcion : msg.OccurredAt;
        ordenCompra.MarcarComoRecibidaCompleta(fechaRecepcion);

        _db.EventosProcesados.Add(new EventoProcesado
        {
            EventId = msg.EventId,
            TipoEvento = nameof(RecepcionConfirmadaEvent),
            ProcesadoEn = DateTime.UtcNow
        });

        try
        {
            await _db.SaveChangesAsync(context.CancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            _logger.LogInformation(
                "RecepcionConfirmadaEvent {EventId} duplicado por índice único, se descarta. CorrelationId {CorrelationId}",
                msg.EventId,
                correlationId);
            return;
        }

        _logger.LogInformation(
            "OrdenCompra {OrdenCompraId} marcada como recibida completa por RecepcionConfirmadaEvent {EventId}. CorrelationId {CorrelationId}",
            ordenCompra.OrdenCompraId,
            msg.EventId,
            correlationId);
    }
}
