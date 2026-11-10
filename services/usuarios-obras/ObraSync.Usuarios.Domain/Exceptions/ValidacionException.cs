namespace ObraSync.Usuarios.Domain.Exceptions;

/// <summary>
/// Error de validación de datos de entrada (HTTP 400). Su mensaje está pensado para mostrarse al cliente.
/// Hereda de ArgumentException para no romper código ni pruebas que ya la esperan (#136).
/// </summary>
public class ValidacionException : ArgumentException
{
    public ValidacionException(string message) : base(message)
    {
    }
}
