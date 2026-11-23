using System;

namespace ObraSync.Requisiciones.Application.Exceptions;

/// <summary>
/// Los parámetros de paginación (página o tamaño) están fuera del rango permitido.
/// </summary>
public class PaginacionInvalidaException : ArgumentException
{
    public PaginacionInvalidaException(string message)
        : base(message)
    {
    }
}
