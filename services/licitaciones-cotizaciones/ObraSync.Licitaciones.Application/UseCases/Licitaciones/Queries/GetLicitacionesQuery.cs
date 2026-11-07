namespace ObraSync.Licitaciones.Application.UseCases.Licitaciones.Queries;

public record GetLicitacionesQuery(
    Guid? ObraId = null,
    string? Estado = null,
    string? RolUsuario = null,
    // Id del usuario (claim 'sub') cuando es Proveedor: sirve para marcar qué pliegos ya cotizó.
    Guid? ProveedorId = null);
