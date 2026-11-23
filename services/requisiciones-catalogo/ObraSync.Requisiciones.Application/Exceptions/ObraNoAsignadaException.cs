using System;

namespace ObraSync.Requisiciones.Application.Exceptions;

/// <summary>
/// El Capataz autenticado intentó crear una requisición para una obra que no tiene asignada (#155).
/// </summary>
public class ObraNoAsignadaException : Exception
{
    public ObraNoAsignadaException(string message)
        : base(message)
    {
    }
}
