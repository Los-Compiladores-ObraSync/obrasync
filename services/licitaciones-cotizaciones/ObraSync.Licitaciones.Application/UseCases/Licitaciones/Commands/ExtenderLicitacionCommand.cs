using MediatR;
using ObraSync.Licitaciones.Application.DTOs;

namespace ObraSync.Licitaciones.Application.UseCases.Licitaciones.Commands;

/// <summary>Mueve la fecha límite de cotización de una licitación abierta a una fecha posterior.</summary>
public record ExtenderLicitacionCommand(
    Guid LicitacionId,
    DateTime NuevaFechaLimiteCotizacion) : IRequest<LicitacionResponseDto>;
