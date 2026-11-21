namespace ObraSync.Licitaciones.Application.DTOs;

public record UpsertCalificacionProveedorRequestDto(int Calificacion);

public record UpsertCalificacionOrdenCompraRequestDto(
    int Calificacion,
    string? Comentario = null
);

public record ComentarioCalificacionProveedorDto(
    Guid? OrdenCompraId,
    int Calificacion,
    string Comentario,
    DateTime FechaActualizacion
);

/// <summary>
/// Calificación vigente del proveedor: promedio (1 a 5, con decimales) de las calificaciones que recibió,
/// cuántas son y los últimos comentarios (#88, #214, #222).
/// </summary>
public record CalificacionProveedorResponseDto(
    Guid ProveedorId,
    decimal Calificacion,
    int CantidadCalificaciones,
    DateTime FechaActualizacion,
    IReadOnlyList<ComentarioCalificacionProveedorDto>? UltimosComentarios = null
);

/// <summary>
/// Reputación propia consultada por el Proveedor autenticado (#222).
/// Si todavía no tiene calificaciones devuelve Calificacion = null, CantidadCalificaciones = 0 y lista vacía.
/// </summary>
public record ReputacionProveedorResponseDto(
    Guid ProveedorId,
    decimal? Calificacion,
    int CantidadCalificaciones,
    DateTime? FechaActualizacion,
    IReadOnlyList<ComentarioCalificacionProveedorDto> UltimosComentarios
);

/// <summary>
/// Calificación registrada por un usuario (Comprador/Admin) para una orden de compra recibida completa (#222).
/// </summary>
public record CalificacionOrdenCompraResponseDto(
    Guid OrdenCompraId,
    Guid ProveedorId,
    Guid UsuarioId,
    int Calificacion,
    string? Comentario,
    DateTime FechaActualizacion,
    decimal PromedioProveedor,
    int CantidadCalificacionesProveedor
);
