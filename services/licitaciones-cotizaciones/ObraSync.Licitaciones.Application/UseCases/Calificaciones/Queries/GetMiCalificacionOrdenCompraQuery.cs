using MediatR;
using ObraSync.Licitaciones.Application.DTOs;

namespace ObraSync.Licitaciones.Application.UseCases.Calificaciones.Queries;

public record GetMiCalificacionOrdenCompraQuery(
    Guid OrdenCompraId,
    Guid UsuarioId
) : IRequest<CalificacionOrdenCompraResponseDto>;
