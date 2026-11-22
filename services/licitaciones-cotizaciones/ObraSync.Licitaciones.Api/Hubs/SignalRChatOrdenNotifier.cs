using Microsoft.AspNetCore.SignalR;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.UseCases.Chat;

namespace ObraSync.Licitaciones.Api.Hubs;

public class SignalRChatOrdenNotifier : IChatOrdenNotifier
{
    private readonly IHubContext<ChatOrdenHub> _hubContext;

    public SignalRChatOrdenNotifier(IHubContext<ChatOrdenHub> hubContext)
    {
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
    }

    public Task NotificarMensajeAsync(MensajeChatOrdenDto mensaje, CancellationToken cancellationToken = default)
        => _hubContext.Clients
            .Group(ChatOrdenAutorizacionHelper.FormarGrupoOrden(mensaje.OrdenCompraId))
            .SendAsync("MensajeRecibido", mensaje, cancellationToken);

    public Task NotificarLecturaAsync(LecturaChatResponseDto lectura, CancellationToken cancellationToken = default)
        => _hubContext.Clients
            .Group(ChatOrdenAutorizacionHelper.FormarGrupoOrden(lectura.OrdenCompraId))
            .SendAsync("LecturaActualizada", lectura, cancellationToken);

    public Task NotificarEstadoProblemaAsync(ProblemaOrdenEstadoDto estadoProblema, CancellationToken cancellationToken = default)
        => _hubContext.Clients
            .Group(ChatOrdenAutorizacionHelper.FormarGrupoOrden(estadoProblema.OrdenCompraId))
            .SendAsync("ProblemaEstadoActualizado", estadoProblema, cancellationToken);
}
