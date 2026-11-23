using System;

namespace ObraSync.Requisiciones.Api.Exceptions;

/// <summary>
/// El token JWT autenticado no contiene un claim de identificador de usuario ('sub') válido con formato GUID.
/// </summary>
public class UsuarioNoIdentificadoException : Exception
{
    public UsuarioNoIdentificadoException(
        string message = "El token JWT no contiene un claim 'sub' válido con formato GUID.")
        : base(message)
    {
    }
}
