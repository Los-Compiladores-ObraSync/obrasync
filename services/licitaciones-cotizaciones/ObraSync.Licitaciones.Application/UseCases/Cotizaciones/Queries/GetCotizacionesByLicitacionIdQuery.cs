using MediatR;
using ObraSync.Licitaciones.Application.DTOs;

namespace ObraSync.Licitaciones.Application.UseCases.Cotizaciones.Queries;

public record GetCotizacionesByLicitacionIdQuery(
    Guid LicitacionId,
    ParametrosPagina? Paginacion = null,
    decimal? PesoPrecio = null,
    decimal? PesoFlete = null,
    decimal? PesoPlazo = null,
    decimal? PesoCalificacion = null) : IRequest<PaginaDe<CotizacionResponseDto>>;
