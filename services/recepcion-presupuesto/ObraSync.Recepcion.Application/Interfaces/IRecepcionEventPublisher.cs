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

    /// <summary>
    /// Construye y publica el RecepcionConProblemaEvent cuando se registra una entrega con problema
    /// en estado Rechazada (#217). Las fotos no viajan en el evento; solo la cantidad.
    /// </summary>
    Task PublishRecepcionConProblemaAsync(
        RecepcionEntity recepcion,
        int cantidadFotos,
        Guid correlationId,
        Guid? proveedorId = null,
        CancellationToken cancellationToken = default);
}

