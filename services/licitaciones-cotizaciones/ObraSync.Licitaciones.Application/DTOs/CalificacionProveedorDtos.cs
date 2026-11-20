namespace ObraSync.Licitaciones.Application.DTOs;

public record UpsertCalificacionProveedorRequestDto(int Calificacion);

/// <summary>
/// Calificación vigente del proveedor: promedio (1 a 5, con decimales) de las calificaciones que recibió
/// de distintos usuarios, y cuántas son.
/// </summary>
public record CalificacionProveedorResponseDto(
    Guid ProveedorId,
    decimal Calificacion,
    int CantidadCalificaciones,
    DateTime FechaActualizacion
);
