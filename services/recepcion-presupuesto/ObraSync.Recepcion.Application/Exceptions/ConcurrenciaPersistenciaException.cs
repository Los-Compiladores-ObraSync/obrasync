namespace ObraSync.Recepcion.Application.Exceptions;

/// <summary>
/// Excepción de aplicación lanzada cuando la capa de persistencia detecta un conflicto de concurrencia optimista.
/// Desacopla a los casos de uso de las excepciones específicas del ORM.
/// </summary>
public class ConcurrenciaPersistenciaException : Exception
{
    public ConcurrenciaPersistenciaException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
