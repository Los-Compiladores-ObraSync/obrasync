using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.Mappings;
using ObraSync.Licitaciones.Application.UseCases.OrdenesCompra.Queries;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.UseCases.OrdenesCompra.Handlers;

public class GetOrdenesCompraQueryHandler
{
    private readonly IOrdenCompraRepository _ordenCompraRepository;
    private readonly ICalificacionProveedorRepository? _calificacionRepository;

    public GetOrdenesCompraQueryHandler(
        IOrdenCompraRepository ordenCompraRepository,
        ICalificacionProveedorRepository? calificacionRepository = null)
    {
        _ordenCompraRepository = ordenCompraRepository ?? throw new ArgumentNullException(nameof(ordenCompraRepository));
        _calificacionRepository = calificacionRepository;
    }

    public async Task<PaginaDe<OrdenCompraResponseDto>> Handle(
        GetOrdenesCompraQuery query,
        CancellationToken cancellationToken = default)
    {
        var esProveedor = string.Equals(query.RolUsuario, "Proveedor", StringComparison.OrdinalIgnoreCase);
        Guid? proveedorEfectivo = query.ProveedorId;

        if (esProveedor)
        {
            if (query.UsuarioId is not Guid uid || uid == Guid.Empty)
            {
                return new PaginaDe<OrdenCompraResponseDto>(Array.Empty<OrdenCompraResponseDto>(), 0);
            }

            proveedorEfectivo = uid;
        }

        var pagina = await _ordenCompraRepository.GetFilteredAsync(
            query.ObraId,
            query.LicitacionId,
            proveedorEfectivo,
            query.Estado,
            query.Paginacion,
            cancellationToken);

        IReadOnlyDictionary<Guid, CalificacionProveedor> misCalificaciones = new Dictionary<Guid, CalificacionProveedor>();
        if (!esProveedor &&
            _calificacionRepository is not null &&
            query.UsuarioId is Guid usuarioId &&
            usuarioId != Guid.Empty &&
            pagina.Items.Count > 0)
        {
            var ordenIds = pagina.Items.Select(o => o.OrdenCompraId).ToList();
            misCalificaciones = await _calificacionRepository.GetByOrdenIdsYUsuarioAsync(
                ordenIds,
                usuarioId,
                cancellationToken);
        }

        var dtos = pagina.Items
            .Select(oc => oc.ToDto(misCalificaciones.GetValueOrDefault(oc.OrdenCompraId)))
            .ToList();
        return new PaginaDe<OrdenCompraResponseDto>(dtos, pagina.Total);
    }
}
