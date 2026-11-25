using MediatR;
using Microsoft.Extensions.Configuration;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.Mappings;
using ObraSync.Licitaciones.Application.Services;
using ObraSync.Licitaciones.Application.UseCases.Cotizaciones.Queries;
using ObraSync.Licitaciones.Domain.Scoring;

namespace ObraSync.Licitaciones.Application.UseCases.Cotizaciones.Handlers;

public class GetCotizacionesByLicitacionIdQueryHandler
    : IRequestHandler<GetCotizacionesByLicitacionIdQuery, PaginaDe<CotizacionResponseDto>>
{
    private readonly ILicitacionRepository _licitacionRepository;
    private readonly ICotizacionRepository _cotizacionRepository;
    private readonly ICalificacionProveedorRepository? _calificacionRepository;
    private readonly ScoringOptions _scoringOptions;

    public GetCotizacionesByLicitacionIdQueryHandler(
        ILicitacionRepository licitacionRepository,
        ICotizacionRepository cotizacionRepository,
        IConfiguration? configuration = null,
        ScoringOptions? scoringOptions = null,
        ICalificacionProveedorRepository? calificacionRepository = null)
    {
        _licitacionRepository = licitacionRepository ?? throw new ArgumentNullException(nameof(licitacionRepository));
        _cotizacionRepository = cotizacionRepository ?? throw new ArgumentNullException(nameof(cotizacionRepository));
        _calificacionRepository = calificacionRepository;
        _scoringOptions = scoringOptions ?? configuration.ReadAndValidateScoringOptions();
        _scoringOptions.Validar();
    }

    public async Task<PaginaDe<CotizacionResponseDto>> Handle(
        GetCotizacionesByLicitacionIdQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.LicitacionId == Guid.Empty)
        {
            throw new ArgumentException("El parámetro licitacionId es obligatorio.");
        }

        var p = query.Paginacion ?? new ParametrosPagina();

        // Validar ponderaciones antes de consultar la base (si el cliente envió pesos negativos o todos cero, falla con ArgumentException -> 400)
        var ponderacion = new PonderacionScoring(
            precio: query.PesoPrecio ?? _scoringOptions.PesoPrecio,
            flete: query.PesoFlete ?? _scoringOptions.PesoFlete,
            plazo: query.PesoPlazo ?? _scoringOptions.PesoPlazo,
            calificacion: query.PesoCalificacion ?? _scoringOptions.PesoCalificacion);

        var licitacion = await _licitacionRepository.GetByIdAsync(query.LicitacionId, cancellationToken);
        if (licitacion == null)
        {
            throw new KeyNotFoundException($"No se encontró la licitación con Id '{query.LicitacionId}'.");
        }

        var cotizaciones = await _cotizacionRepository.GetByLicitacionIdAsync(query.LicitacionId, cancellationToken);
        if (cotizaciones == null || !cotizaciones.Any())
        {
            return new PaginaDe<CotizacionResponseDto>(Array.Empty<CotizacionResponseDto>(), 0);
        }

        var proveedorIds = cotizaciones
            .Select(c => c.ProveedorId)
            .Distinct()
            .ToList();

        IReadOnlyDictionary<Guid, ResumenCalificacionProveedor> resumenesPorProveedor = new Dictionary<Guid, ResumenCalificacionProveedor>();
        IReadOnlyDictionary<Guid, decimal> calificacionesPorProveedor = new Dictionary<Guid, decimal>();
        if (_calificacionRepository != null)
        {
            resumenesPorProveedor = await _calificacionRepository.GetResumenesByProveedorIdsAsync(proveedorIds, cancellationToken)
                ?? new Dictionary<Guid, ResumenCalificacionProveedor>();

            if (resumenesPorProveedor.Count > 0)
            {
                calificacionesPorProveedor = resumenesPorProveedor.ToDictionary(kv => kv.Key, kv => kv.Value.Promedio);
            }
            else
            {
                calificacionesPorProveedor = await _calificacionRepository.GetPromediosByProveedorIdsAsync(proveedorIds, cancellationToken)
                    ?? new Dictionary<Guid, decimal>();
            }
        }

        // Solo las cotizaciones completas (con ítems válidos > 0 y sin cobertura parcial) compiten por el scoring y EsGanadora (#186 / #190 / #197)
        var cotizacionesCompletas = cotizaciones
            .Where(c =>
                c.Items != null &&
                c.Items.Count > 0 &&
                c.Items.All(i => i.PrecioUnitario > 0) &&
                !c.Items.Any(i => i.CoberturaParcial))
            .ToList();

        // 1. Mapeamos usando el constructor posicional de la entidad Oferta del Dominio (pasando CantidadOfrecida para #190 y Calificacion para #88)
        var ofertasDomain = cotizacionesCompletas.Select(c =>
        {
            decimal? calif = calificacionesPorProveedor.TryGetValue(c.ProveedorId, out var valorCalif)
                ? valorCalif
                : null;

            return new Oferta(
                c.CotizacionId,
                c.ProveedorId,
                c.Items.Select(i => new OfertaItem(
                    i.ArticuloId,
                    i.PrecioUnitario,
                    i.CantidadOfrecida
                )).ToList(),
                c.Flete,
                c.PlazoEntregaDias,
                calif
            );
        }).ToList();

        var itemsRequeridos = licitacion.Items.Select(i => new ItemRequerido(
            i.ArticuloId,
            i.Cantidad
        )).ToList();

        // 2. Ejecutamos el motor de scoring sobre las ofertas completas de la licitación
        var resultadosScoring = ofertasDomain.Count > 0
            ? CalculadorScoring.Calcular(ofertasDomain, itemsRequeridos, ponderacion, _scoringOptions.ModoNormalizacion)
            : Array.Empty<ResultadoScoring>();

        // 3. Regla de negocio de la tarjeta #73 + #197: Con menos de 2 cotizaciones válidas que entren al scoring NO se marca ganadora a ninguna
        bool evaluarGanadora = resultadosScoring.Count >= 2;

        var ordenadasConDesempate = resultadosScoring
            .Join(cotizacionesCompletas, s => s.CotizacionId, c => c.CotizacionId, (s, c) => new { Scoring = s, Cotizacion = c })
            .OrderByDescending(x => x.Scoring.Puntaje)
            .ThenBy(x => x.Cotizacion.PlazoEntregaDias)
            .ThenBy(x => x.Cotizacion.FechaCreacion)
            .ThenBy(x => x.Cotizacion.CotizacionId)
            .ToList();

        Guid? idGanadora = (evaluarGanadora && ordenadasConDesempate.Any())
            ? ordenadasConDesempate.First().Cotizacion.CotizacionId
            : null;

        // 4. Cálculo de costos totales (precio de ítems + flete) y resaltado de la oferta más barata (CU-07, Tarjeta #89 + #190)
        var cantidadesPorArticulo = licitacion.Items
            .GroupBy(i => i.ArticuloId)
            .ToDictionary(g => g.Key, g => g.First().Cantidad);

        var pesoTotal = ponderacion.Total;
        var pesoPrecioNorm = Math.Round(ponderacion.Precio / pesoTotal, 4);
        var pesoFleteNorm = Math.Round(ponderacion.Flete / pesoTotal, 4);
        var pesoPlazoNorm = Math.Round(ponderacion.Plazo / pesoTotal, 4);
        var pesoCalificacionNorm = Math.Round(ponderacion.Calificacion / pesoTotal, 4);

        // Preparamos el DTO de ponderaciones normalizadas aplicadas en el scoring (#88 + #197 + #206)
        var ponderacionesDto = new CriteriosScoringDto
        {
            Precio = pesoPrecioNorm,
            Flete = pesoFleteNorm,
            Plazo = pesoPlazoNorm,
            Calificacion = pesoCalificacionNorm
        };

        var dtos = cotizaciones
            .OrderByDescending(c => c.FechaCreacion)
            .ThenBy(c => c.CotizacionId)
            .Select(c =>
            {
                var scoringItem = resultadosScoring.FirstOrDefault(s => s.CotizacionId == c.CotizacionId);
                decimal puntaje = scoringItem?.Puntaje ?? 0m;
                bool esGanadora = idGanadora.HasValue && c.CotizacionId == idGanadora.Value;

                var dto = c.ToDto();
                decimal subtotalItems = 0m;
                foreach (var itemDto in dto.Items)
                {
                    var cantidadPliego = cantidadesPorArticulo.TryGetValue(itemDto.ArticuloId, out var cant)
                        ? cant
                        : 0m;
                    var cantidadEfectiva = itemDto.CantidadOfrecida is > 0 ? itemDto.CantidadOfrecida.Value : cantidadPliego;
                    itemDto.SubtotalItem = cantidadEfectiva * itemDto.PrecioUnitario;
                    subtotalItems += itemDto.SubtotalItem;
                }

                decimal costoTotal = subtotalItems + c.Flete;

                // Desglose REAL (#206): cuántos puntos (0 a 100) aportó cada criterio. Los cuatro suman puntajeTotal.
                CriteriosScoringDto? desgloseDto = scoringItem?.Aporte is { } aporte
                    ? DesglosarPuntos(aporte)
                    : null;

                DesgloseScoringDto? desgloseScoringDto = scoringItem?.PuntajesCriterio is { } pc
                    ? new DesgloseScoringDto
                    {
                        PuntajePrecio = Math.Round(pc.Precio * 100m, 2),
                        PuntajePlazo = Math.Round(pc.Plazo * 100m, 2),
                        PuntajeFlete = Math.Round(pc.Flete * 100m, 2),
                        PuntajeCalificacion = Math.Round(pc.Calificacion * 100m, 2),
                        PesoPrecio = pesoPrecioNorm,
                        PesoPlazo = pesoPlazoNorm,
                        PesoFlete = pesoFleteNorm,
                        PesoCalificacion = pesoCalificacionNorm
                    }
                    : null;

                decimal? calificacionProveedor = calificacionesPorProveedor.TryGetValue(c.ProveedorId, out var califGuardada)
                    ? califGuardada
                    : null;

                int cantidadCalificacionesProveedor = resumenesPorProveedor.TryGetValue(c.ProveedorId, out var resumenProveedor)
                    ? resumenProveedor.Cantidad
                    : (calificacionProveedor.HasValue ? 1 : 0);

                dto.SubtotalItems = subtotalItems;
                dto.CostoTotal = costoTotal;
                dto.CoberturaParcial = c.Items.Any(i => i.CoberturaParcial);
                dto.PuntajeTotal = Math.Round(puntaje * 100m, 2);
                dto.EsGanadora = esGanadora;
                dto.CalificacionProveedor = calificacionProveedor;
                dto.CantidadCalificacionesProveedor = cantidadCalificacionesProveedor;
                dto.DesglosePuntaje = desgloseDto;
                dto.PonderacionesScoring = ponderacionesDto;
                dto.DesgloseScoring = desgloseScoringDto;

                return dto;
            }).ToList();

        // Una oferta con cobertura parcial nunca le roba EsMasBarata a las que cubren todo lo pedido (#190)
        var dtosValidosParaMinimo = dtos
            .Where(d => d.Items.Count > 0 && d.SubtotalItems > 0 && !d.CoberturaParcial)
            .ToList();

        if (dtosValidosParaMinimo.Count > 0)
        {
            decimal minCostoTotal = dtosValidosParaMinimo.Min(d => d.CostoTotal);
            foreach (var dto in dtosValidosParaMinimo)
            {
                dto.EsMasBarata = dto.CostoTotal == minCostoTotal;
            }
        }

        var total = dtos.Count;
        var paginaItems = dtos
            .Skip(p.Saltear)
            .Take(p.Tamano)
            .ToList();

        return new PaginaDe<CotizacionResponseDto>(paginaItems, total);
    }

    /// <summary>
    /// Pasa los aportes (0 a 1) a puntos (0 a 100) con 2 decimales, igual que puntajeTotal. Para que la suma de los cuatro dé
    /// EXACTAMENTE puntajeTotal, la diferencia por redondeo se le suma al criterio que más aportó.
    /// </summary>
    internal static CriteriosScoringDto DesglosarPuntos(AporteScoring aporte)
    {
        var puntos = new[] { aporte.Precio, aporte.Flete, aporte.Plazo, aporte.Calificacion }
            .Select(a => Math.Round(a * 100m, 2))
            .ToArray();
        var total = Math.Round((aporte.Precio + aporte.Flete + aporte.Plazo + aporte.Calificacion) * 100m, 2);

        var diferencia = total - puntos.Sum();
        if (diferencia != 0m)
        {
            var mayor = Array.IndexOf(puntos, puntos.Max());
            puntos[mayor] += diferencia;
        }

        return new CriteriosScoringDto
        {
            Precio = puntos[0],
            Flete = puntos[1],
            Plazo = puntos[2],
            Calificacion = puntos[3]
        };
    }
}
