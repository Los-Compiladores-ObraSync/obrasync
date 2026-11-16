using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Domain.Scoring;

namespace ObraSync.Licitaciones.Application.UseCases.Cotizaciones.Queries;

public record GetCotizacionesByLicitacionIdQuery(
    Guid LicitacionId,
    ParametrosPagina? Paginacion = null,
    PonderacionScoring? Ponderacion = null);
