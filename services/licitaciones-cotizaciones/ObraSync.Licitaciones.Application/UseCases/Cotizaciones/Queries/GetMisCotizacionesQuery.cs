using MediatR;
using ObraSync.Licitaciones.Application.DTOs;

namespace ObraSync.Licitaciones.Application.UseCases.Cotizaciones.Queries;

public record GetMisCotizacionesQuery(
    Guid ProveedorId,
    ParametrosPagina? Paginacion = null) : IRequest<PaginaDe<MiCotizacionResponseDto>>;
