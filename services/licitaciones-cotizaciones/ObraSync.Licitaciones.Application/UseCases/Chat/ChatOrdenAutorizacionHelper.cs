using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.UseCases.Chat;

/// <summary>
/// Centraliza la validación de existencia y permisos de acceso al chat por orden de compra (#218),
/// reutilizada por los handlers REST y el Hub SignalR para evitar reglas duplicadas.
/// </summary>
public static class ChatOrdenAutorizacionHelper
{
    public static string? NormalizarRolChat(string? rolUsuario)
    {
        if (string.IsNullOrWhiteSpace(rolUsuario))
        {
            return null;
        }

        if (string.Equals(rolUsuario.Trim(), "Comprador", StringComparison.OrdinalIgnoreCase))
        {
            return "Comprador";
        }

        if (string.Equals(rolUsuario.Trim(), "Admin", StringComparison.OrdinalIgnoreCase))
        {
            return "Admin";
        }

        if (string.Equals(rolUsuario.Trim(), "Proveedor", StringComparison.OrdinalIgnoreCase))
        {
            return "Proveedor";
        }

        return null;
    }

    public static async Task<(OrdenCompra OrdenCompra, string RolNormalizado)> ValidarAccesoChatAsync(
        IOrdenCompraRepository ordenCompraRepository,
        Guid ordenCompraId,
        Guid usuarioId,
        string? rolUsuario,
        CancellationToken cancellationToken = default)
    {
        if (ordenCompraId == Guid.Empty)
        {
            throw new ArgumentException("El identificador de la orden de compra es obligatorio.", nameof(ordenCompraId));
        }

        if (usuarioId == Guid.Empty)
        {
            throw new ArgumentException("El identificador del usuario autenticado es obligatorio.", nameof(usuarioId));
        }

        var rolNormalizado = NormalizarRolChat(rolUsuario)
            ?? throw new UnauthorizedAccessException(
                "Solo Comprador, Admin o el proveedor titular de la orden pueden acceder al chat de esta orden de compra.");

        var ordenCompra = await ordenCompraRepository.GetByIdAsync(ordenCompraId, cancellationToken)
            ?? throw new KeyNotFoundException($"No se encontró la orden de compra con ID {ordenCompraId}.");

        if (rolNormalizado == "Proveedor" && ordenCompra.ProveedorId != usuarioId)
        {
            throw new UnauthorizedAccessException(
                "Solo el proveedor titular de la orden de compra puede acceder al chat de esta orden.");
        }

        return (ordenCompra, rolNormalizado);
    }

    public static string FormarGrupoOrden(Guid ordenCompraId) => $"orden-{ordenCompraId:D}";
}
