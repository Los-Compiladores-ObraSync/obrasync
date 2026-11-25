namespace ObraSync.Recepcion.Application.Exceptions;

/// <summary>
/// Excepción de aplicación lanzada cuando la capa de persistencia detecta una violación de índice o restricción única.
/// Desacopla a los casos de uso de <c>DbUpdateException</c> / <c>SqlException</c>.
/// </summary>
public class IndiceUnicoPersistenciaException : Exception
{
    public IndiceUnicoPersistenciaException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
