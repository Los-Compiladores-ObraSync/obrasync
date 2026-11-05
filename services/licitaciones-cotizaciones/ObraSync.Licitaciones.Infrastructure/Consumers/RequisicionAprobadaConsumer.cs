using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ObraSync.Contracts.Events;
using ObraSync.Licitaciones.Domain.Entities;
using Microsoft.Data.SqlClient;

namespace ObraSync.Licitaciones.Infrastructure.Consumers;

public class RequisicionAprobadaConsumer : IConsumer<RequisicionAprobadaEvent>
{
    private const int EtapaMaxLength = 200;

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
        // La etapa es opcional, pero si viene no puede pasar de 200 caracteres (tamaño de la columna)
        if (msg.EventId == Guid.Empty || msg.Items is null || msg.Items.Count == 0 ||
            (msg.Etapa is not null && msg.Etapa.Trim().Length > EtapaMaxLength))
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

        // La requisición se guarda una sola vez, aunque llegue con otro EventId
        var yaExiste = await _db.RequisicionesAprobadas.AnyAsync(r => r.RequisicionId == msg.RequisicionId);
        if (!yaExiste)
        {
            _db.RequisicionesAprobadas.Add(new RequisicionAprobada
            {
                RequisicionId = msg.RequisicionId,
                NumeroRequisicion = msg.NumeroRequisicion ?? "",
                ObraId = msg.ObraId,
                Etapa = string.IsNullOrWhiteSpace(msg.Etapa) ? null : msg.Etapa.Trim(),
                RecibidaEnUtc = DateTime.UtcNow,
                Items = msg.Items.Select(i => new RequisicionAprobadaItem
                {
                    Id = Guid.NewGuid(),
                    ArticuloId = i.ArticuloId,
                    Descripcion = i.Descripcion ?? "",
                    Cantidad = (decimal)i.Cantidad,
                    UnidadMedida = i.UnidadMedida
                }).ToList()
            });
        }
        else
        {
            _logger.LogInformation("Requisicion {RequisicionId} ya estaba guardada", msg.RequisicionId);
        }

        try
        {
            // Un solo guardado: el evento procesado y los datos entran juntos o no entra nada
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Dos copias llegaron a la vez: el índice único frenó la segunda
            _logger.LogInformation("Evento {EventId} duplicado (índice único), se descarta", msg.EventId);
            return;
        }

        _logger.LogInformation(
            "RequisicionAprobada recibida. Requisicion {RequisicionId}, Obra {ObraId}, CorrelationId {CorrelationId}",
            msg.RequisicionId, msg.ObraId, msg.CorrelationId);

        // El pliego lo publica el Comprador a mano (tarjeta #69): acá solo se guarda la requisición.
    }
}