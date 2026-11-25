namespace ObraSync.Usuarios.Application.DTOs;

/// <summary>Proyección liviana de lectura para listados paginados de usuarios activos por rol (#254).</summary>
public sealed record UsuarioResumenDto(Guid Id, string NombreCompleto, string? Telefono = null);
