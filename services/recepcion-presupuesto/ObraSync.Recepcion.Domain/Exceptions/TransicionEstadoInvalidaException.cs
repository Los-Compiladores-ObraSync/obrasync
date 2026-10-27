namespace ObraSync.Recepcion.Domain.Exceptions;

public class TransicionEstadoInvalidaException : Exception
{
    public TransicionEstadoInvalidaException(string mensaje)
        : base(mensaje)
    {
    }
}