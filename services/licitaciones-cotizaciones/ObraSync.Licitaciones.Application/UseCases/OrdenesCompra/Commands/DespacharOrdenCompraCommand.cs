using MediatR;
using ObraSync.Licitaciones.Application.DTOs;

namespace ObraSync.Licitaciones.Application.UseCases.OrdenesCompra.Commands;

public record DespacharOrdenCompraCommand(
    Guid OrdenCompraId,
    Guid UsuarioId,
    Guid CorrelationId = default,
    DateTime? FechaEstimadaLlegada = null,
    string? Observacion = null) : IRequest<OrdenCompraResponseDto>;
