namespace ObraSync.Licitaciones.Application.UseCases.Licitaciones.Queries;

public record GetLicitacionesQuery(
    Guid? ObraId = null,
    string? Estado = null,
    string? RolUsuario = null);
