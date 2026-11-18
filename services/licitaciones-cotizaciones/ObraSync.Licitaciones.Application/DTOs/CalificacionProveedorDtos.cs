namespace ObraSync.Licitaciones.Application.DTOs;

public record UpsertCalificacionProveedorRequestDto(int Calificacion);

public record CalificacionProveedorResponseDto(
    Guid ProveedorId,
    int Calificacion,
    DateTime FechaActualizacion
);
