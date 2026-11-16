using ObraSync.Licitaciones.Application.DTOs;

namespace ObraSync.Licitaciones.Application.UseCases.Cotizaciones.Queries;

public record GetCotizacionesByLicitacionIdQuery(
    Guid LicitacionId,
    ParametrosPagina? Paginacion = null);
