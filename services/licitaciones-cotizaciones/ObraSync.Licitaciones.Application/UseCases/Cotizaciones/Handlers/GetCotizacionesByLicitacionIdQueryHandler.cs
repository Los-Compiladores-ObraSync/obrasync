using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.Mappings;
using ObraSync.Licitaciones.Application.UseCases.Cotizaciones.Queries;
using ObraSync.Licitaciones.Domain.Scoring;

namespace ObraSync.Licitaciones.Application.UseCases.Cotizaciones.Handlers;

public class GetCotizacionesByLicitacionIdQueryHandler
{
    private readonly ILicitacionRepository _licitacionRepository;
    private readonly ICotizacionRepository _cotizacionRepository;

    public GetCotizacionesByLicitacionIdQueryHandler(
        ILicitacionRepository licitacionRepository,
        ICotizacionRepository cotizacionRepository)
    {
        _licitacionRepository = licitacionRepository ?? throw new ArgumentNullException(nameof(licitacionRepository));
        _cotizacionRepository = cotizacionRepository ?? throw new ArgumentNullException(nameof(cotizacionRepository));
    }

    public async Task<IReadOnlyList<CotizacionResponseDto>> Handle(
        GetCotizacionesByLicitacionIdQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.LicitacionId == Guid.Empty)
        {
            throw new ArgumentException("El parámetro licitacionId es obligatorio.");
        }

        var licitacion = await _licitacionRepository.GetByIdAsync(query.LicitacionId, cancellationToken);
        if (licitacion == null)
        {
            throw new KeyNotFoundException($"No se encontró la licitación con Id '{query.LicitacionId}'.");
        }

        var cotizaciones = await _cotizacionRepository.GetByLicitacionIdAsync(query.LicitacionId, cancellationToken);
        if (cotizaciones == null || !cotizaciones.Any())
        {
            return Array.Empty<CotizacionResponseDto>();
        }

        // 1. Mapeamos usando el constructor posicional exacto de la entidad Oferta del Dominio
        var ofertasDomain = cotizaciones.Select(c => new Oferta(
            c.CotizacionId,
            c.ProveedorId,
            c.Items.Select(i => new OfertaItem(
                i.ArticuloId,
                i.PrecioUnitario
            )).ToList(),
            c.Flete,
            c.PlazoEntregaDias,
            null
        )).ToList();

        var itemsRequeridos = licitacion.Items.Select(i => new ItemRequerido(
            i.ArticuloId,
            i.Cantidad
        )).ToList();

        var ponderacion = new PonderacionScoring(0.5m, 0.2m, 0.2m, 0.1m);

        // 2. Ejecutamos el motor de scoring
        var resultadosScoring = CalculadorScoring.Calcular(ofertasDomain, itemsRequeridos, ponderacion);

        // 3. Regla de negocio de la tarjeta #73: Con menos de 2 cotizaciones NO se marca ganadora a ninguna
        bool evaluarGanadora = cotizaciones.Count >= 2;

        var ordenadasConDesempate = resultadosScoring
            .Join(cotizaciones, s => s.CotizacionId, c => c.CotizacionId, (s, c) => new { Scoring = s, Cotizacion = c })
            .OrderByDescending(x => x.Scoring.Puntaje)
            .ThenBy(x => x.Cotizacion.PlazoEntregaDias)
            .ThenBy(x => x.Cotizacion.FechaCreacion)
            .ToList();

        Guid? idGanadora = (evaluarGanadora && ordenadasConDesempate.Any()) 
            ? ordenadasConDesempate.First().Cotizacion.CotizacionId 
            : null;

        // 4. Mapeamos al DTO incluyendo los nuevos campos del contrato OpenAPI
        return cotizaciones.Select(c => 
        {
            var scoringItem = resultadosScoring.FirstOrDefault(s => s.CotizacionId == c.CotizacionId);
            decimal puntaje = scoringItem?.Puntaje ?? 0m;
            bool esGanadora = idGanadora.HasValue && c.CotizacionId == idGanadora.Value;

            var dto = c.ToDto();
            dto.PuntajeTotal = Math.Round(puntaje * 100m, 2);
            dto.EsGanadora = esGanadora;

            return dto;
        }).ToList();
    }
}