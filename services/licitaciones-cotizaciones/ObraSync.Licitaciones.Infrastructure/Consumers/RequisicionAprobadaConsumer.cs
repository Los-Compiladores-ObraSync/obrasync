using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ObraSync.Contracts.Events;

namespace ObraSync.Licitaciones.Infrastructure.Consumers;

public class RequisicionAprobadaConsumer : IConsumer<RequisicionAprobadaEvent>
{
    private readonly LicitacionesDbContext _db;
    private readonly ILogger<RequisicionAprobadaConsumer> _logger;

    public RequisicionAprobadaConsumer(LicitacionesDbContext db, ILogger<RequisicionAprobadaConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<RequisicionAprobadaEvent> context)
    {
        var msg = context.Message;

        // Datos inválidos: se descartan (reintentarlos no los arregla)
        if (msg.EventId == Guid.Empty || msg.Items is null || msg.Items.Count == 0)
        {
            _logger.LogWarning(
                "RequisicionAprobadaEvent inválido, se descarta. EventId {EventId}, Requisicion {RequisicionId}",
                msg.EventId, msg.RequisicionId);
            return;
        }

        // Idempotencia: si ya lo procesamos, lo descartamos
        if (await _db.EventosProcesados.AnyAsync(e => e.EventId == msg.EventId))
        {
            _logger.LogInformation("Evento {EventId} duplicado, se descarta", msg.EventId);
            return;
        }

        _db.EventosProcesados.Add(new EventoProcesado
        {
            EventId = msg.EventId,
            TipoEvento = nameof(RequisicionAprobadaEvent),
            ProcesadoEn = DateTime.UtcNow
        });

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Dos copias llegaron a la vez: el índice único frenó la segunda
            _logger.LogInformation("Evento {EventId} duplicado (índice único), se descarta", msg.EventId);
            return;
        }

        _logger.LogInformation(
            "RequisicionAprobada recibida. Requisicion {RequisicionId}, Obra {ObraId}, CorrelationId {CorrelationId}",
            msg.RequisicionId, msg.ObraId, msg.CorrelationId);

        // Qué más hace el servicio con el evento no está definido: por ahora solo lo registra.
    }
}