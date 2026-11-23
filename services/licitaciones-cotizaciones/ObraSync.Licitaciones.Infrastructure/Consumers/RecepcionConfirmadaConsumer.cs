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
/// Consume RecepcionConfirmadaEvent (#222 / #218) de forma idempotente (por EventId):
/// - Si la recepción es Rechazada, se ignora para no cerrar problemas ni marcar recepción completa.
/// - Si es válida: cierra cualquier problema abierto en la orden, y si OrdenCompleta == true marca la orden
///   como recibida completa y agrega el mensaje del sistema en el chat ("Llegó todo el pedido", sin publicar MensajeChatEnviadoEvent).
/// </summary>
public class RecepcionConfirmadaConsumer : IConsumer<RecepcionConfirmadaEvent>
{
    private readonly LicitacionesDbContext _db;
    private readonly ILogger<RecepcionConfirmadaConsumer> _logger;
    private readonly IChatOrdenRepository? _chatOrdenRepository;
    private readonly IChatOrdenNotifier? _chatOrdenNotifier;

    public RecepcionConfirmadaConsumer(
        LicitacionesDbContext db,
        ILogger<RecepcionConfirmadaConsumer> logger,
        IChatOrdenRepository? chatOrdenRepository = null,
        IChatOrdenNotifier? chatOrdenNotifier = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _chatOrdenRepository = chatOrdenRepository;
        _chatOrdenNotifier = chatOrdenNotifier;
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

        if (esRechazada)
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
                "RecepcionConfirmadaEvent {EventId} en estado Rechazada para OrdenCompra {OrdenCompraId} ignorado. CorrelationId {CorrelationId}",
                msg.EventId,
                msg.OrdenCompraId,
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

        var ahoraUtc = DateTime.UtcNow;
        var fechaRecepcion = msg.FechaRecepcion != default
            ? msg.FechaRecepcion
            : (msg.OccurredAt != default ? msg.OccurredAt : ahoraUtc);

        var problemaCerrado = ordenCompra.ResolverProblema(fechaRecepcion, null);

        MensajeChatOrden? mensajeSistema = null;
        if (msg.OrdenCompleta)
        {
            if (ordenCompra.MarcarComoRecibidaCompleta(fechaRecepcion))
            {
                mensajeSistema = MensajeChatOrden.CrearDelSistema(
                    ordenCompra.OrdenCompraId,
                    "Llegó todo el pedido",
                    ahoraUtc);

                if (_chatOrdenRepository is not null)
                {
                    await _chatOrdenRepository.AddMensajeAsync(mensajeSistema, context.CancellationToken);
                }
                else
                {
                    _db.MensajesChatOrden.Add(mensajeSistema);
                }
            }
        }
        else
        {
            ordenCompra.RegistrarRecepcionParcial(fechaRecepcion);
        }

        _db.EventosProcesados.Add(new EventoProcesado
        {
            EventId = msg.EventId,
            TipoEvento = nameof(RecepcionConfirmadaEvent),
            ProcesadoEn = ahoraUtc
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

        if (_chatOrdenNotifier is not null && (mensajeSistema is not null || problemaCerrado))
        {
            try
            {
                if (mensajeSistema is not null)
                {
                    await _chatOrdenNotifier.NotificarMensajeAsync(mensajeSistema.ToDto(), context.CancellationToken);
                }

                if (problemaCerrado)
                {
                    await _chatOrdenNotifier.NotificarEstadoProblemaAsync(
                        ordenCompra.ToProblemaEstadoDto(),
                        context.CancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "No se pudo notificar por SignalR el impacto de RecepcionConfirmadaEvent en OrdenCompra {OrdenCompraId}. CorrelationId {CorrelationId}",
                    ordenCompra.OrdenCompraId,
                    correlationId);
            }
        }

        _logger.LogInformation(
            "RecepcionConfirmadaEvent {EventId} procesado para OrdenCompra {OrdenCompraId} (OrdenCompleta={OrdenCompleta}, ProblemaCerrado={ProblemaCerrado}). CorrelationId {CorrelationId}",
            msg.EventId,
            ordenCompra.OrdenCompraId,
            msg.OrdenCompleta,
            problemaCerrado,
            correlationId);
    }
}
