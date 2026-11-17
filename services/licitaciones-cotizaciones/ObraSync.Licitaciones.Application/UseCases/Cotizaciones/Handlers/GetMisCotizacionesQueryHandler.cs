using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.UseCases.Cotizaciones.Queries;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.UseCases.Cotizaciones.Handlers;

public class GetMisCotizacionesQueryHandler
{
    private readonly ICotizacionRepository _cotizacionRepository;
    private readonly ILicitacionRepository _licitacionRepository;
    private readonly IOrdenCompraRepository _ordenCompraRepository;

    public GetMisCotizacionesQueryHandler(
        ICotizacionRepository cotizacionRepository,
        ILicitacionRepository licitacionRepository,
        IOrdenCompraRepository ordenCompraRepository)
    {
        _cotizacionRepository = cotizacionRepository ?? throw new ArgumentNullException(nameof(cotizacionRepository));
        _licitacionRepository = licitacionRepository ?? throw new ArgumentNullException(nameof(licitacionRepository));
        _ordenCompraRepository = ordenCompraRepository ?? throw new ArgumentNullException(nameof(ordenCompraRepository));
    }

    public async Task<PaginaDe<MiCotizacionResponseDto>> Handle(
        GetMisCotizacionesQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.ProveedorId == Guid.Empty)
        {
            throw new ArgumentException("El identificador del proveedor es obligatorio.");
        }

        var paginaCotizaciones = await _cotizacionRepository.GetByProveedorIdAsync(
            query.ProveedorId,
            query.Paginacion,
            cancellationToken);

        var cotizaciones = paginaCotizaciones.Items;
        if (cotizaciones == null || cotizaciones.Count == 0)
        {
            return new PaginaDe<MiCotizacionResponseDto>(Array.Empty<MiCotizacionResponseDto>(), paginaCotizaciones.Total);
        }

        var licitacionIds = cotizaciones
            .Select(c => c.LicitacionId)
            .Distinct()
            .ToList();

        var licitacionesPorId = new Dictionary<Guid, Licitacion>();
        foreach (var licitacionId in licitacionIds)
        {
            var licitacion = await _licitacionRepository.GetByIdAsync(licitacionId, cancellationToken);
            if (licitacion != null)
            {
                licitacionesPorId[licitacionId] = licitacion;
            }
        }

        var ordenesCompra = await _ordenCompraRepository.GetByLicitacionIdsAsync(licitacionIds, cancellationToken);
        var ordenesPorLicitacion = ordenesCompra
            .GroupBy(oc => oc.LicitacionId)
            .ToDictionary(g => g.Key, g => g.First());

        var resultado = new List<MiCotizacionResponseDto>(cotizaciones.Count);

        foreach (var cotizacion in cotizaciones)
        {
            licitacionesPorId.TryGetValue(cotizacion.LicitacionId, out var licitacion);

            var cantidadesPorArticulo = licitacion != null
                ? licitacion.Items
                    .GroupBy(i => i.ArticuloId)
                    .ToDictionary(g => g.Key, g => g.First().Cantidad)
                : new Dictionary<Guid, decimal>();

            var itemsDto = cotizacion.Items
                .Select(i =>
                {
                    var cantPliego = cantidadesPorArticulo.TryGetValue(i.ArticuloId, out var cantidad)
                        ? cantidad
                        : 0m;
                    var subtotalItem = i.CalcularSubtotal(cantPliego);
                    return new CrearCotizacionItemDto
                    {
                        ArticuloId = i.ArticuloId,
                        PrecioUnitario = i.PrecioUnitario,
                        PresentacionOfrecida = i.PresentacionOfrecida,
                        CantidadOfrecida = i.CantidadOfrecida,
                        SubtotalItem = subtotalItem,
                        CoberturaParcial = i.CoberturaParcial
                    };
                })
                .ToList();

            decimal subtotalItems = itemsDto.Sum(i => i.SubtotalItem);
            decimal costoTotal = subtotalItems + cotizacion.Flete;

            var estadoLicitacionEnum = licitacion?.Estado ?? EstadoLicitacion.Abierta;
            bool esGanadora = estadoLicitacionEnum == EstadoLicitacion.Adjudicada &&
                ordenesPorLicitacion.TryGetValue(cotizacion.LicitacionId, out var ordenCompra) &&
                (ordenCompra.CotizacionId == cotizacion.CotizacionId || ordenCompra.ProveedorId == cotizacion.ProveedorId);

            string estadoResultado = estadoLicitacionEnum switch
            {
                EstadoLicitacion.Abierta => "Enviada",
                EstadoLicitacion.Adjudicada => esGanadora ? "Gano" : "NoGano",
                EstadoLicitacion.Cerrada => "LicitacionCerrada",
                _ => "Enviada"
            };

            resultado.Add(new MiCotizacionResponseDto
            {
                CotizacionId = cotizacion.CotizacionId,
                LicitacionId = cotizacion.LicitacionId,
                ProveedorId = cotizacion.ProveedorId,
                NumeroRequisicion = licitacion?.NumeroRequisicion ?? string.Empty,
                ObraId = licitacion?.ObraId ?? Guid.Empty,
                FechaLimiteCotizacion = licitacion != null
                    ? DateTime.SpecifyKind(licitacion.FechaLimiteCotizacion, DateTimeKind.Utc)
                    : DateTime.SpecifyKind(cotizacion.FechaCreacion, DateTimeKind.Utc),
                EstadoLicitacion = estadoLicitacionEnum.ToString(),
                SubtotalItems = subtotalItems,
                Flete = cotizacion.Flete,
                CostoTotal = costoTotal,
                CoberturaParcial = cotizacion.Items.Any(i => i.CoberturaParcial),
                PlazoEntregaDias = cotizacion.PlazoEntregaDias,
                FechaCreacion = DateTime.SpecifyKind(cotizacion.FechaCreacion, DateTimeKind.Utc),
                Items = itemsDto,
                EsGanadora = esGanadora,
                EstadoResultado = estadoResultado
            });
        }

        return new PaginaDe<MiCotizacionResponseDto>(resultado, paginaCotizaciones.Total);
    }
}
