using MassTransit;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ObraSync.Contracts.Events;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.Mappings;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Infrastructure.Consumers;

/// <summary>
/// Consume RecepcionConProblemaEvent (#217 / #218) de forma idempotente (por EventId):
/// abre la marca de problema en OrdenCompra y agrega el mensaje del sistema en el chat de la orden
/// (sin publicar MensajeChatEnviadoEvent).
/// </summary>
public class RecepcionConProblemaConsumer : IConsumer<RecepcionConProblemaEvent>
{
    private readonly LicitacionesDbContext _db;
    private readonly ILogger<RecepcionConProblemaConsumer> _logger;
    private readonly IChatOrdenNotifier? _chatOrdenNotifier;

    public RecepcionConProblemaConsumer(
        LicitacionesDbContext db,
        ILogger<RecepcionConProblemaConsumer> logger,
        IChatOrdenNotifier? chatOrdenNotifier = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _chatOrdenNotifier = chatOrdenNotifier;
    }

    public async Task Consume(ConsumeContext<RecepcionConProblemaEvent> context)
    {
        var msg = context.Message;
        var correlationId = msg.CorrelationId != Guid.Empty
            ? msg.CorrelationId
            : (context.CorrelationId ?? Guid.Empty);

        using var logScope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        });

        if (msg.EventId == Guid.Empty || msg.OrdenCompraId == Guid.Empty || string.IsNullOrWhiteSpace(msg.Motivo))
        {
            _logger.LogWarning(
                "RecepcionConProblemaEvent inválido (EventId, OrdenCompraId o Motivo vacío), se descarta. EventId {EventId}, OrdenCompraId {OrdenCompraId}, CorrelationId {CorrelationId}",
                msg.EventId,
                msg.OrdenCompraId,
                correlationId);
            return;
        }

        if (await _db.EventosProcesados.AnyAsync(e => e.EventId == msg.EventId, context.CancellationToken))
        {
            _logger.LogInformation(
                "RecepcionConProblemaEvent {EventId} duplicado, se descarta. CorrelationId {CorrelationId}",
                msg.EventId,
                correlationId);
            return;
        }

        var ordenCompra = await _db.OrdenesCompra
            .FirstOrDefaultAsync(o => o.OrdenCompraId == msg.OrdenCompraId, context.CancellationToken);

        if (ordenCompra is null)
        {
            _logger.LogWarning(
                "No se encontró la OrdenCompra {OrdenCompraId} al consumir RecepcionConProblemaEvent {EventId}. Se ignora sin error. CorrelationId {CorrelationId}",
                msg.OrdenCompraId,
                msg.EventId,
                correlationId);
            return;
        }

        var ahoraUtc = DateTime.UtcNow;
        var fechaProblema = msg.OccurredAt != default ? msg.OccurredAt : ahoraUtc;
        var motivoLimpio = msg.Motivo.Trim();

        ordenCompra.AbrirProblema(motivoLimpio, fechaProblema);

        var mensajeSistema = MensajeChatOrden.CrearDelSistema(
            ordenCompra.OrdenCompraId,
            $"El Capataz registró un problema: {motivoLimpio}",
            ahoraUtc);

        _db.MensajesChatOrden.Add(mensajeSistema);
        _db.EventosProcesados.Add(new EventoProcesado
        {
            EventId = msg.EventId,
            TipoEvento = nameof(RecepcionConProblemaEvent),
            ProcesadoEn = ahoraUtc
        });

        try
        {
            await _db.SaveChangesAsync(context.CancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            _logger.LogInformation(
                "RecepcionConProblemaEvent {EventId} duplicado por índice único, se descarta. CorrelationId {CorrelationId}",
                msg.EventId,
                correlationId);
            return;
        }

        if (_chatOrdenNotifier is not null)
        {
            try
            {
                await _chatOrdenNotifier.NotificarMensajeAsync(mensajeSistema.ToDto(), context.CancellationToken);
                await _chatOrdenNotifier.NotificarEstadoProblemaAsync(ordenCompra.ToProblemaEstadoDto(), context.CancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "No se pudo notificar por SignalR el problema registrado en OrdenCompra {OrdenCompraId}. CorrelationId {CorrelationId}",
                    ordenCompra.OrdenCompraId,
                    correlationId);
            }
        }

        _logger.LogInformation(
            "Problema registrado en chat y estado de OrdenCompra {OrdenCompraId} por RecepcionConProblemaEvent {EventId}. Motivo: {Motivo}. CorrelationId {CorrelationId}",
            ordenCompra.OrdenCompraId,
            msg.EventId,
            motivoLimpio,
            correlationId);
    }
}
