using ObraSync.Licitaciones.Application.DTOs;
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

    /// <summary>Resumen (promedio, cantidad y última fecha) de cada proveedor que tenga calificaciones (#222).</summary>
    Task<IReadOnlyDictionary<Guid, ResumenCalificacionProveedor>> GetResumenesByProveedorIdsAsync(
        IReadOnlyCollection<Guid> proveedorIds,
        CancellationToken cancellationToken = default);

    /// <summary>Guarda la calificación legacy (#88) de un usuario a un proveedor (OrdenCompraId = null).</summary>
    Task<CalificacionProveedor> UpsertAsync(
        Guid proveedorId,
        Guid calificadorId,
        int calificacion,
        DateTime fechaActualizacionUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Guarda o actualiza la calificación de un usuario para una orden de compra específica (#222).</summary>
    Task<CalificacionProveedor> UpsertPorOrdenAsync(
        Guid ordenCompraId,
        Guid proveedorId,
        Guid calificadorId,
        int calificacion,
        string? comentario,
        DateTime fechaActualizacionUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Obtiene la calificación que el usuario dio a una orden de compra específica (#222), o null.</summary>
    Task<CalificacionProveedor?> GetByOrdenYUsuarioAsync(
        Guid ordenCompraId,
        Guid calificadorId,
        CancellationToken cancellationToken = default);

    /// <summary>Obtiene en lote las calificaciones que un usuario registró para un conjunto de órdenes (#222).</summary>
    Task<IReadOnlyDictionary<Guid, CalificacionProveedor>> GetByOrdenIdsYUsuarioAsync(
        IReadOnlyCollection<Guid> ordenCompraIds,
        Guid calificadorId,
        CancellationToken cancellationToken = default);

    /// <summary>Últimos comentarios no vacíos recibidos por un proveedor, ordenados del más reciente al más antiguo (#222).</summary>
    Task<IReadOnlyList<ComentarioCalificacionProveedorDto>> GetUltimosComentariosAsync(
        Guid proveedorId,
        int limite = 20,
        CancellationToken cancellationToken = default);
}
