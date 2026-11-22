namespace ObraSync.Usuarios.Domain.Exceptions;

public class ServicioNoDisponibleException : Exception
{
    public ServicioNoDisponibleException(string message) : base(message) { }
}
