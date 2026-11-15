using ObraSync.Asistente.Api.Dtos;
using ObraSync.Asistente.Application;

namespace ObraSync.Asistente.Api.Controllers;

/// <summary>Reglas del contrato: mensaje de 1 a 500 caracteres, historial de hasta 10 mensajes con rol válido.</summary>
public static class ValidadorChat
{
    /// <summary>Devuelve el motivo del rechazo, o null si el pedido es válido.</summary>
    public static string? Validar(ChatRequestDto? pedido)
    {
        if (pedido is null || string.IsNullOrWhiteSpace(pedido.Mensaje))
            return "El mensaje no puede estar vacío.";
        if (pedido.Mensaje.Length > Agente.MaxCaracteresEntrada)
            return $"El mensaje no puede superar los {Agente.MaxCaracteresEntrada} caracteres.";

        var historial = pedido.Historial;
        if (historial is null)
            return null;
        if (historial.Count > Agente.MaxMensajesHistorial)
            return $"El historial no puede tener más de {Agente.MaxMensajesHistorial} mensajes.";

        foreach (var m in historial)
        {
            if (m is null || (m.Rol != "usuario" && m.Rol != "asistente"))
                return "Cada mensaje del historial debe tener rol 'usuario' o 'asistente'.";
            if (string.IsNullOrWhiteSpace(m.Texto))
                return "Los mensajes del historial no pueden estar vacíos.";
            if (m.Texto.Length > Agente.MaxCaracteresHistorial)
                return $"Un mensaje del historial no puede superar los {Agente.MaxCaracteresHistorial} caracteres.";
        }

        return null;
    }
}
