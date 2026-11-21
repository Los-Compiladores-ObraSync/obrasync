using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.Mappings;
using ObraSync.Licitaciones.Application.UseCases.Licitaciones.Queries;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.UseCases.Licitaciones.Handlers;

public class GetLicitacionesQueryHandler
{
    private readonly ILicitacionRepository _repository;
    private readonly ICotizacionRepository _cotizacionRepository;
    private readonly IOrdenCompraRepository? _ordenCompraRepository;

    public GetLicitacionesQueryHandler(
        ILicitacionRepository repository,
        ICotizacionRepository cotizacionRepository,
        IOrdenCompraRepository? ordenCompraRepository = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _cotizacionRepository = cotizacionRepository ?? throw new ArgumentNullException(nameof(cotizacionRepository));
        _ordenCompraRepository = ordenCompraRepository;
    }

    public async Task<PaginaDe<LicitacionResponseDto>> Handle(
        GetLicitacionesQuery query,
        CancellationToken cancellationToken = default)
    {
        var esProveedor = string.Equals(query.RolUsuario, "Proveedor", StringComparison.OrdinalIgnoreCase);
        var estadoEfectivo = esProveedor
            ? EstadoLicitacion.Abierta.ToString()
            : query.Estado;

        var pagina = await _repository.GetFilteredAsync(
            query.ObraId,
            estadoEfectivo,
            query.RolUsuario,
            query.Paginacion,
            cancellationToken);

        var dtos = pagina.Items.Select(l => l.ToDto()).ToList();

        // Solo al Proveedor se le marca cuáles ya cotizó (una consulta para toda la lista).
        // Comprador y Admin reciben null: para ellos el dato no tiene sentido.
        if (esProveedor && query.ProveedorId is Guid proveedorId && dtos.Count > 0)
        {
            var cotizadas = await _cotizacionRepository.GetLicitacionIdsCotizadasPorProveedorAsync(
                proveedorId,
                dtos.Select(d => d.LicitacionId).ToList(),
                cancellationToken);
            var cotizadasSet = cotizadas.ToHashSet();

            foreach (var dto in dtos)
            {
                dto.YaCotizada = cotizadasSet.Contains(dto.LicitacionId);
            }
        }

        // Cotización adjudicada (#222): en lote para las licitaciones adjudicadas de la página.
        if (!esProveedor && _ordenCompraRepository is not null && dtos.Count > 0)
        {
            var adjudicadasIds = dtos
                .Where(d => string.Equals(d.Estado, EstadoLicitacion.Adjudicada.ToString(), StringComparison.OrdinalIgnoreCase))
                .Select(d => d.LicitacionId)
                .ToList();

            if (adjudicadasIds.Count > 0)
            {
                var ordenes = await _ordenCompraRepository.GetByLicitacionIdsAsync(adjudicadasIds, cancellationToken);
                var cotizacionPorLicitacion = ordenes
                    .GroupBy(o => o.LicitacionId)
                    .ToDictionary(g => g.Key, g => g.First().CotizacionId);

                foreach (var dto in dtos)
                {
                    if (cotizacionPorLicitacion.TryGetValue(dto.LicitacionId, out var cotizacionAdjudicadaId))
                    {
                        dto.CotizacionAdjudicadaId = cotizacionAdjudicadaId;
                    }
                }
            }
        }

        return new PaginaDe<LicitacionResponseDto>(dtos, pagina.Total);
    }
}
