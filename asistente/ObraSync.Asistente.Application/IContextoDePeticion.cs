namespace ObraSync.Asistente.Application;

/// <summary>
/// Datos del pedido que se está atendiendo, para que las herramientas llamen al backend
/// "en nombre del usuario": con SU token (nunca una cuenta propia) y con el mismo X-Correlation-ID.
/// La implementación real lee el HttpContext (capa Api); en las pruebas se reemplaza.
/// </summary>
public interface IContextoDePeticion
{
    /// <summary>El JWT del usuario que escribe, sin el prefijo "Bearer ". Null si no vino.</summary>
    string? Jwt { get; }

    /// <summary>Identificador para rastrear el pedido entre servicios (X-Correlation-ID).</summary>
    string CorrelationId { get; }
}
