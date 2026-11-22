using ObraSync.Licitaciones.Application.DTOs;

namespace ObraSync.Licitaciones.Application.Interfaces;

/// <summary>
/// Emite notificaciones en tiempo real a los clientes suscriptos al grupo SignalR de la orden de compra (#218).
/// </summary>
public interface IChatOrdenNotifier
{
    Task NotificarMensajeAsync(MensajeChatOrdenDto mensaje, CancellationToken cancellationToken = default);

    Task NotificarLecturaAsync(LecturaChatResponseDto lectura, CancellationToken cancellationToken = default);

    Task NotificarEstadoProblemaAsync(ProblemaOrdenEstadoDto estadoProblema, CancellationToken cancellationToken = default);
}
