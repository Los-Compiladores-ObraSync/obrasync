using MassTransit;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ObraSync.Contracts.Events;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Domain.Presupuestos;
using ObraSync.Recepcion.Infrastructure.Configuration;
using ObraSync.Recepcion.Infrastructure.Persistence;

namespace ObraSync.Recepcion.Infrastructure.Consumers;

/// <summary>
/// Crea el presupuesto semilla de cada etapa de una obra nueva (o ya existente) que todavía no tenga partida.
/// Idempotente: por EventId y porque nunca duplica la partida de una etapa. Con monto semilla 0 no hace nada.
/// </summary>
public class ObraCreadaConsumer : IConsumer<ObraCreadaEvent>
{
    private readonly RecepcionDbContext _db;
    private readonly ILogger<ObraCreadaConsumer> _logger;
    private readonly decimal _montoSemilla;

    public ObraCreadaConsumer(
        RecepcionDbContext db,
        ILogger<ObraCreadaConsumer> logger,
        IOptions<PresupuestoSemillaOptions>? opciones = null)
    {
        _db = db;
        _logger = logger;
        _montoSemilla = opciones?.Value.MontoSemillaPorEtapa ?? 0m;
    }

    public async Task Consume(ConsumeContext<ObraCreadaEvent> context)
    {
        var msg = context.Message;
        if (msg.EventId == Guid.Empty || msg.ObraId == Guid.Empty || _montoSemilla <= 0)
            return;

        if (await _db.EventosProcesados.AnyAsync(e => e.EventId == msg.EventId, context.CancellationToken))
            return;

        var partidas = await _db.PartidasPresupuestarias
            .Where(p => p.ObraId == msg.ObraId)
            .ToListAsync(context.CancellationToken);

        var etapas = (msg.Etapas ?? Array.Empty<string>())
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string?>()
            .ToList();
        if (etapas.Count == 0) etapas.Add(null);

        var creadas = 0;
        foreach (var etapa in etapas)
        {
            if (EmparejadorPartidaPresupuestaria.BuscarPartida(partidas, etapa) is not null) continue;
            var nueva = PartidaSemilla.Crear(msg.ObraId, etapa, _montoSemilla);
            partidas.Add(nueva);
            _db.PartidasPresupuestarias.Add(nueva);
            creadas++;
        }

        _db.EventosProcesados.Add(new EventoProcesado
        {
            EventId = msg.EventId,
            TipoEvento = nameof(ObraCreadaEvent),
            ProcesadoEn = DateTime.UtcNow
        });

        try
        {
            await _db.SaveChangesAsync(context.CancellationToken);
            _logger.LogInformation("Obra {ObraId}: {Creadas} partidas semilla creadas.", msg.ObraId, creadas);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            _logger.LogInformation("ObraCreadaEvent {EventId} duplicado, se descarta.", msg.EventId);
        }
    }
}
