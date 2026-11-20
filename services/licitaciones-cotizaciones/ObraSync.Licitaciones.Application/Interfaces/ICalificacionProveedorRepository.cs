using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.Interfaces;

/// <summary>Calificación de un proveedor: promedio de las calificaciones que recibió.</summary>
public record ResumenCalificacionProveedor(
    Guid ProveedorId,
    decimal Promedio,
    int Cantidad,
    DateTime FechaActualizacion);

public interface ICalificacionProveedorRepository
{
    /// <summary>Promedio, cantidad y última fecha de las calificaciones del proveedor; null si no tiene ninguna.</summary>
    Task<ResumenCalificacionProveedor?> GetResumenAsync(
        Guid proveedorId,
        CancellationToken cancellationToken = default);

    /// <summary>Promedio (2 decimales) de cada proveedor que tenga calificaciones.</summary>
    Task<IReadOnlyDictionary<Guid, decimal>> GetPromediosByProveedorIdsAsync(
        IReadOnlyCollection<Guid> proveedorIds,
        CancellationToken cancellationToken = default);

    /// <summary>Guarda la calificación de un usuario a un proveedor: crea la fila o actualiza la que ese usuario ya había dado.</summary>
    Task<CalificacionProveedor> UpsertAsync(
        Guid proveedorId,
        Guid calificadorId,
        int calificacion,
        DateTime fechaActualizacionUtc,
        CancellationToken cancellationToken = default);
}
