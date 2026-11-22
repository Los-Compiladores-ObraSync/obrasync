using System.Security.Claims;

namespace ObraSync.Licitaciones.Api.Hubs;

internal static class JwtUserClaimsHelper
{
    public static bool TryGetUsuarioId(ClaimsPrincipal? user, out Guid usuarioId)
    {
        usuarioId = Guid.Empty;
        if (user is null)
        {
            return false;
        }

        var sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out usuarioId) && usuarioId != Guid.Empty;
    }

    public static string? ObtenerRolUsuario(ClaimsPrincipal? user)
    {
        if (user is null)
        {
            return null;
        }

        if (user.IsInRole("Comprador"))
        {
            return "Comprador";
        }

        if (user.IsInRole("Admin"))
        {
            return "Admin";
        }

        if (user.IsInRole("Proveedor"))
        {
            return "Proveedor";
        }

        return user.FindFirstValue("role") ?? user.FindFirstValue(ClaimTypes.Role);
    }

    public static string? ObtenerNombreUsuario(ClaimsPrincipal? user)
    {
        if (user is null)
        {
            return null;
        }

        return user.FindFirstValue("name")
            ?? user.FindFirstValue(ClaimTypes.Name)
            ?? user.FindFirstValue("email")
            ?? user.FindFirstValue(ClaimTypes.Email);
    }
}
