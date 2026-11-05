using MediatR;
using ObraSync.Licitaciones.Application.DTOs;

namespace ObraSync.Licitaciones.Application.UseCases.Adjudicar.Commands;

public record AdjudicarCotizacionCommand(Guid LicitacionId, Guid CotizacionId) : IRequest<OrdenCompraResponseDto>;