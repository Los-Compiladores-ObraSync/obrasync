namespace ObraSync.Usuarios.Domain.Exceptions;

/// <summary>
/// Conflicto con el estado actual del recurso, por ejemplo un correo ya registrado (HTTP 409).
/// Su mensaje está pensado para mostrarse al cliente. Hereda de InvalidOperationException para no
/// romper código ni pruebas que ya la esperan (#136).
/// </summary>
public class ConflictoException : InvalidOperationException
{
    public ConflictoException(string message) : base(message)
    {
    }
}
