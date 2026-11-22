namespace ObraSync.Contracts.Events;

/// <summary>
/// Evento de integración publicado por Licitaciones y Cotizaciones cuando un usuario envía un mensaje humano en el chat de una Orden de Compra (#218).
/// Cumple con contracts/events/MensajeChatEnviadoEvent.json (#218, #227).
/// Es una copia propia del servicio porque el contexto de build de Docker no incluye la carpeta contracts de la raíz del repo (#132).
/// </summary>
public record MensajeChatEnviadoEvent(
    Guid EventId,
    DateTime OccurredAt,
    Guid CorrelationId,
    Guid OrdenCompraId,
    RemitenteChatEvent Remitente,
    DestinatariosChatEvent Destinatarios,
    string Fragmento
);

public record RemitenteChatEvent(
    Guid UsuarioId,
    string Nombre,
    string Rol
);

public record DestinatariosChatEvent(
    IReadOnlyList<Guid> UsuarioIds,
    IReadOnlyList<string> Roles
);
