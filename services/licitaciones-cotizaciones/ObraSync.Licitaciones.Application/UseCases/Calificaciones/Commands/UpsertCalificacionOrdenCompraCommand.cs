using MediatR;
using ObraSync.Licitaciones.Application.DTOs;

namespace ObraSync.Licitaciones.Application.UseCases.Calificaciones.Commands;

public record UpsertCalificacionOrdenCompraCommand(
    Guid OrdenCompraId,
    Guid UsuarioId,
    int Calificacion,
    string? Comentario = null
) : IRequest<CalificacionOrdenCompraResponseDto>;
