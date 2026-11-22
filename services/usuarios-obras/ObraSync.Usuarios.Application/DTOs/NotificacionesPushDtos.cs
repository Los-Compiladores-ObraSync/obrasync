namespace ObraSync.Usuarios.Application.DTOs;

public record VapidPublicKeyResponseDto(string PublicKey);

public record CrearSuscripcionPushRequestDto(
    string? Endpoint,
    string? P256dh,
    string? Auth,
    string? Navegador = null
);

public record SuscripcionPushResponseDto(
    Guid Id,
    Guid UsuarioId,
    string Endpoint,
    string? Navegador,
    DateTime FechaAlta,
    DateTime? UltimoEnvio
);

public record PayloadNotificacionPushDto(
    string Titulo,
    string Cuerpo,
    string Ruta,
    string Tipo,
    DateTime TimestampUtc
);
