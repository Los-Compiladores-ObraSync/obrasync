using MediatR;
using ObraSync.Licitaciones.Application.DTOs;

namespace ObraSync.Licitaciones.Application.UseCases.Calificaciones.Commands;

public record UpsertCalificacionProveedorCommand(
    Guid ProveedorId,
    int Calificacion) : IRequest<CalificacionProveedorResponseDto>;
