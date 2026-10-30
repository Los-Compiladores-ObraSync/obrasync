using RecepcionEntity = ObraSync.Recepcion.Domain.Entities.Recepcion;

namespace ObraSync.Recepcion.Application.Interfaces;

public interface IRecepcionEventPublisher
{
    /// <summary>
    /// Construye y publica el RecepcionConfirmadaEvent a partir de una recepción ya guardada.
    /// El correlationId llega ya parseado y validado por el llamador.
    /// </summary>
    Task PublishRecepcionConfirmadaAsync(
        RecepcionEntity recepcion,
        Guid correlationId,
        CancellationToken cancellationToken = default);
}

