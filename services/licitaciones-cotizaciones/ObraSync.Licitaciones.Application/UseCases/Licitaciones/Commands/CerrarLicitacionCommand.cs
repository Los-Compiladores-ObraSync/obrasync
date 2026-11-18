using MediatR;
using ObraSync.Licitaciones.Application.DTOs;

namespace ObraSync.Licitaciones.Application.UseCases.Licitaciones.Commands;

public record CerrarLicitacionCommand(
    Guid LicitacionId,
    string? Motivo) : IRequest<LicitacionResponseDto>;
