namespace ObraSync.Usuarios.Application.DTOs;

/// <summary>Proveedor activo que Compras puede elegir (por ejemplo, para generarle un Magic Link de cotización, #157).</summary>
public record ProveedorDto(Guid Id, string NombreCompleto);
