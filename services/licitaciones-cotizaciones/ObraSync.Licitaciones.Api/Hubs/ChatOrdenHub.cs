using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.UseCases.Chat;

namespace ObraSync.Licitaciones.Api.Hubs;

[Authorize(Roles = "Comprador,Admin,Proveedor")]
public class ChatOrdenHub : Hub
{
    private readonly IOrdenCompraRepository _ordenCompraRepository;

    public ChatOrdenHub(IOrdenCompraRepository ordenCompraRepository)
    {
        _ordenCompraRepository = ordenCompraRepository ?? throw new ArgumentNullException(nameof(ordenCompraRepository));
    }

    public async Task UnirseAOrden(Guid ordenCompraId)
    {
        if (!JwtUserClaimsHelper.TryGetUsuarioId(Context.User, out var usuarioId))
        {
            throw new HubException("El token JWT no contiene un claim 'sub' válido con formato GUID.");
        }

        var rolUsuario = JwtUserClaimsHelper.ObtenerRolUsuario(Context.User);

        try
        {
            await ChatOrdenAutorizacionHelper.ValidarAccesoChatAsync(
                _ordenCompraRepository,
                ordenCompraId,
                usuarioId,
                rolUsuario,
                Context.ConnectionAborted);
        }
        catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException or UnauthorizedAccessException)
        {
            throw new HubException(ex.Message);
        }

        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            ChatOrdenAutorizacionHelper.FormarGrupoOrden(ordenCompraId),
            Context.ConnectionAborted);
    }

    public Task SalirDeOrden(Guid ordenCompraId)
    {
        if (ordenCompraId == Guid.Empty)
        {
            return Task.CompletedTask;
        }

        return Groups.RemoveFromGroupAsync(
            Context.ConnectionId,
            ChatOrdenAutorizacionHelper.FormarGrupoOrden(ordenCompraId),
            Context.ConnectionAborted);
    }
}
