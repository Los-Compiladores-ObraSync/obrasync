using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.Interfaces;

public interface ICotizacionRepository
{
    Task<bool> ExistsByLicitacionAndProveedorAsync(
        Guid licitacionId,
        Guid proveedorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// De las licitaciones indicadas, devuelve los Id de las que el proveedor ya cotizó.
    /// Una sola consulta para toda la lista (evita consultar de a una licitación).
    /// </summary>
    Task<IReadOnlyCollection<Guid>> GetLicitacionIdsCotizadasPorProveedorAsync(
        Guid proveedorId,
        IReadOnlyCollection<Guid> licitacionIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Cotizacion>> GetByLicitacionIdAsync(
        Guid licitacionId,
        CancellationToken cancellationToken = default);

    Task<PaginaDe<Cotizacion>> GetByProveedorIdAsync(
        Guid proveedorId,
        ParametrosPagina? paginacion = null,
        CancellationToken cancellationToken = default);

    /// <summary>La cotización de un proveedor para una licitación, con sus ítems y SEGUIDA por el contexto (para poder corregirla).</summary>
    Task<Cotizacion?> GetPropiaParaEditarAsync(
        Guid cotizacionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cambia los ítems de una cotización ya guardada: marca los viejos para borrar y los nuevos como agregados.
    /// Hace falta decírselo al contexto de forma explícita: un ítem nuevo con Id ya asignado, agregado a la lista de una
    /// cotización que EF ya sigue, se tomaría por uno existente (intentaría un UPDATE de algo que no está en la base).
    /// </summary>
    void ReemplazarItems(Cotizacion cotizacion, IReadOnlyList<CotizacionItem> nuevos);

    Task<Cotizacion?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        Cotizacion cotizacion,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(
        CancellationToken cancellationToken = default);
}
