using MediatR;
using ObraSync.Licitaciones.Application.DTOs;

namespace ObraSync.Licitaciones.Application.UseCases.Calificaciones.Queries;

public record GetCalificacionProveedorQuery(Guid ProveedorId) : IRequest<CalificacionProveedorResponseDto>;
