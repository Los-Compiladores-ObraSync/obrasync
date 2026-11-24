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
    private readonly IChatOrdenRepository? _chatOrdenRepository;
    private readonly ICotizacionRepository? _cotizacionRepository;

    public GetOrdenesCompraQueryHandler(
        IOrdenCompraRepository ordenCompraRepository,
        ICalificacionProveedorRepository? calificacionRepository = null,
        IChatOrdenRepository? chatOrdenRepository = null,
        ICotizacionRepository? cotizacionRepository = null)
    {
        _ordenCompraRepository = ordenCompraRepository ?? throw new ArgumentNullException(nameof(ordenCompraRepository));
        _calificacionRepository = calificacionRepository;
        _chatOrdenRepository = chatOrdenRepository;
        _cotizacionRepository = cotizacionRepository;
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
        IReadOnlyDictionary<Guid, int> noLeidosPorOrden = new Dictionary<Guid, int>();
        IReadOnlyDictionary<(Guid CotizacionId, Guid ArticuloId), string?> presentacionesAdjudicadas =
            new Dictionary<(Guid CotizacionId, Guid ArticuloId), string?>();

        if (query.UsuarioId is Guid usuarioId &&
            usuarioId != Guid.Empty &&
            pagina.Items.Count > 0)
        {
            var ordenIds = pagina.Items.Select(o => o.OrdenCompraId).ToList();

            if (!esProveedor && _calificacionRepository is not null)
            {
                misCalificaciones = await _calificacionRepository.GetByOrdenIdsYUsuarioAsync(
                    ordenIds,
                    usuarioId,
                    cancellationToken);
            }

            if (_chatOrdenRepository is not null)
            {
                noLeidosPorOrden = await _chatOrdenRepository.GetConteoNoLeidosPorOrdenIdsAsync(
                    ordenIds,
                    usuarioId,
                    cancellationToken);
            }

        }

        if (_cotizacionRepository is not null && pagina.Items.Count > 0)
        {
            var clavesItems = pagina.Items
                .SelectMany(o => o.Items.Select(item => (o.CotizacionId, item.ArticuloId)))
                .Distinct()
                .ToList();
            presentacionesAdjudicadas = await _cotizacionRepository.GetPresentacionesOfrecidasAsync(
                clavesItems,
                cancellationToken);
        }

        var dtos = pagina.Items
            .Select(oc => oc.ToDto(
                misCalificaciones.GetValueOrDefault(oc.OrdenCompraId),
                noLeidosPorOrden.GetValueOrDefault(oc.OrdenCompraId, 0),
                presentacionesAdjudicadas))
            .ToList();
        return new PaginaDe<OrdenCompraResponseDto>(dtos, pagina.Total);
    }
}
