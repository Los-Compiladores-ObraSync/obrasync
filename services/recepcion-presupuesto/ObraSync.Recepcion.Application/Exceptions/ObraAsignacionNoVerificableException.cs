using System;

namespace ObraSync.Recepcion.Application.Exceptions;

/// <summary>
/// Excepción lanzada cuando no se puede verificar la asignación de una obra al Capataz
/// debido a fallas de comunicación, timeout o errores internos del servicio Usuarios y Obras (#189, #155).
/// El controlador debe traducir esta excepción a un código HTTP 503 Service Unavailable (RFC 7807).
/// </summary>
public class ObraAsignacionNoVerificableException : Exception
{
    public ObraAsignacionNoVerificableException(string message) : base(message) { }

    public ObraAsignacionNoVerificableException(string message, Exception? innerException)
        : base(message, innerException) { }
}
