namespace ObraSync.Recepcion.Application.Exceptions;

public class RecepcionConflictoException : InvalidOperationException
{
    public RecepcionConflictoException(string message)
        : base(message)
    {
    }
}
