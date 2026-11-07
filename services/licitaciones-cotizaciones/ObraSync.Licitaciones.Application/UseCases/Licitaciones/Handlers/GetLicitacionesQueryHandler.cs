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

    public GetLicitacionesQueryHandler(
        ILicitacionRepository repository,
        ICotizacionRepository cotizacionRepository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _cotizacionRepository = cotizacionRepository ?? throw new ArgumentNullException(nameof(cotizacionRepository));
    }

    public async Task<IReadOnlyList<LicitacionResponseDto>> Handle(
        GetLicitacionesQuery query,
        CancellationToken cancellationToken = default)
    {
        var esProveedor = string.Equals(query.RolUsuario, "Proveedor", StringComparison.OrdinalIgnoreCase);
        var estadoEfectivo = esProveedor
            ? EstadoLicitacion.Abierta.ToString()
            : query.Estado;

        var licitaciones = await _repository.GetFilteredAsync(
            query.ObraId,
            estadoEfectivo,
            query.RolUsuario,
            cancellationToken);

        var dtos = licitaciones.Select(l => l.ToDto()).ToList();

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

        return dtos;
    }
}
