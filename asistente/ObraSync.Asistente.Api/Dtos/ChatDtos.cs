using ObraSync.Asistente.Application;

namespace ObraSync.Asistente.Api.Dtos;

/// <summary>Un mensaje anterior de la conversación (rol: "usuario" o "asistente").</summary>
public sealed record ChatMensajeDto(string? Rol, string? Texto);

/// <summary>Lo que manda el cliente: el mensaje nuevo y los últimos mensajes (el servidor no guarda conversaciones).</summary>
public sealed record ChatRequestDto(string? Mensaje, List<ChatMensajeDto>? Historial);

/// <summary>
/// Lo que devuelve el asistente. "Borrador" viene cuando armó una requisición para confirmar.
/// Su presencia NO significa que se haya creado nada: la escritura la hace el frontend al Confirmar.
/// </summary>
public sealed record ChatResponseDto(string Respuesta, BorradorRequisicion? Borrador, int? TokensUsados);
