using MediatR;
using ObraSync.Licitaciones.Application.DTOs;

namespace ObraSync.Licitaciones.Application.UseCases.Licitaciones.Queries;

public record GetLicitacionByIdQuery(Guid LicitacionId, string? RolUsuario = null)
    : IRequest<LicitacionResponseDto>;
