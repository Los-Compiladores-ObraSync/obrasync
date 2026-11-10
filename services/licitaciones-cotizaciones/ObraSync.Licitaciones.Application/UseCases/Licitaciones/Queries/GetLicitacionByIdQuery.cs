namespace ObraSync.Licitaciones.Application.UseCases.Licitaciones.Queries;

public record GetLicitacionByIdQuery(Guid LicitacionId, string? RolUsuario = null);
