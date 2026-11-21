using MediatR;
using ObraSync.Licitaciones.Application.DTOs;

namespace ObraSync.Licitaciones.Application.UseCases.Calificaciones.Queries;

public record GetMiReputacionProveedorQuery(Guid ProveedorId) : IRequest<ReputacionProveedorResponseDto>;
