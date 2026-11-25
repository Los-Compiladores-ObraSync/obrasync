using MediatR;
using ObraSync.Licitaciones.Application.DTOs;

namespace ObraSync.Licitaciones.Application.UseCases.Licitaciones.Queries;

public record GetSeguimientoRequisicionQuery(Guid RequisicionId)
    : IRequest<SeguimientoRequisicionDto>;
