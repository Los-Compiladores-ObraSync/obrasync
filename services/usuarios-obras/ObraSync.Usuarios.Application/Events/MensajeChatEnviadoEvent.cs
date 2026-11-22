namespace ObraSync.Contracts.Events;

/// <summary>
/// Copia local del contrato contracts/events/MensajeChatEnviadoEvent.json para el consumidor de Usuarios (#227).
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
