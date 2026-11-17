using ObraSync.Asistente.Application;

namespace ObraSync.Asistente.Api.Dtos;

/// <summary>Un mensaje anterior de la conversación (rol: "usuario" o "asistente").</summary>
public sealed record ChatMensajeDto(string? Rol, string? Texto);

/// <summary>Lo que manda el cliente: el mensaje nuevo y los últimos mensajes (el servidor no guarda conversaciones).</summary>
public sealed record ChatRequestDto(string? Mensaje, List<ChatMensajeDto>? Historial);

/// <summary>
/// Lo que devuelve el asistente. "Borrador" viene cuando armó una requisición para confirmar.
/// Su presencia NO significa que se haya creado nada: la escritura la hace el frontend al Confirmar.
/// "Aprobacion" viene cuando el Comprador pidió aprobar una requisición Pendiente: tampoco aprueba nada hasta que confirma.
/// "Recepcion" viene cuando el Capataz pidió registrar lo que llegó (#147): tampoco registra nada hasta que confirma.
/// "Adjudicacion" viene cuando el Comprador pidió adjudicar una oferta (#149): tampoco adjudica ni emite la Orden de Compra hasta que confirma.
/// </summary>
public sealed record ChatResponseDto(
    string Respuesta, BorradorRequisicion? Borrador, int? TokensUsados, BorradorAprobacion? Aprobacion = null,
    BorradorRecepcion? Recepcion = null, BorradorAdjudicacion? Adjudicacion = null);

/// <summary>
/// Lo que se leyó de la foto de un remito (#209). Cada campo es null si no se pudo leer: es una SUGERENCIA para
/// completar el formulario, que la persona revisa y puede corregir. No se guarda nada.
/// "Fecha" viene como yyyy-MM-dd.
/// </summary>
public sealed record LecturaRemitoDto(string? NumeroRemito, string? Fecha);
