namespace ObraSync.Contracts.Events;

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
