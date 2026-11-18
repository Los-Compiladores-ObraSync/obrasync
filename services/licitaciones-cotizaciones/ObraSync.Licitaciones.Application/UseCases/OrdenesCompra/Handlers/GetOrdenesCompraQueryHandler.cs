using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.Mappings;
using ObraSync.Licitaciones.Application.UseCases.OrdenesCompra.Queries;

namespace ObraSync.Licitaciones.Application.UseCases.OrdenesCompra.Handlers;

public class GetOrdenesCompraQueryHandler
{
    private readonly IOrdenCompraRepository _ordenCompraRepository;

    public GetOrdenesCompraQueryHandler(IOrdenCompraRepository ordenCompraRepository)
    {
        _ordenCompraRepository = ordenCompraRepository ?? throw new ArgumentNullException(nameof(ordenCompraRepository));
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

        var dtos = pagina.Items.Select(oc => oc.ToDto()).ToList();
        return new PaginaDe<OrdenCompraResponseDto>(dtos, pagina.Total);
    }
}
