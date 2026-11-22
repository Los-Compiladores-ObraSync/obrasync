using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ObraSync.Usuarios.Domain.Entities;
using ObraSync.Usuarios.Infrastructure.Data;

namespace ObraSync.Usuarios.Infrastructure.Consumers;

/// <summary>
/// Centraliza la persistencia idempotente de EventId en EventosProcesados (#227)
/// ANTES de enviar notificaciones Web Push externas para impedir duplicados ante reentregas o concurrencia.
/// </summary>
internal static class IdempotenciaConsumidorHelper
{
    public static async Task<bool> IntentarRegistrarEventoAsync(
        AuthDbContext db,
        Guid eventId,
        string tipoEvento,
        Guid correlationId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (await db.EventosProcesados.AnyAsync(e => e.EventId == eventId, cancellationToken))
        {
            logger.LogInformation(
                "{TipoEvento} {EventId} duplicado, se descarta. CorrelationId {CorrelationId}",
                tipoEvento,
                eventId,
                correlationId);
            return false;
        }

        db.EventosProcesados.Add(new EventoProcesado
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            TipoEvento = tipoEvento,
            ProcesadoEn = DateTime.UtcNow
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            logger.LogInformation(
                "{TipoEvento} {EventId} duplicado por índice único, se descarta. CorrelationId {CorrelationId}",
                tipoEvento,
                eventId,
                correlationId);
            return false;
        }
    }
}
