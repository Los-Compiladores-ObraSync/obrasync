using MediatR;
using ObraSync.Licitaciones.Application.DTOs;

namespace ObraSync.Licitaciones.Application.UseCases.OrdenesCompra.Commands;

public record DespacharOrdenCompraCommand(
    Guid OrdenCompraId,
    Guid CorrelationId = default) : IRequest<OrdenCompraResponseDto>;
