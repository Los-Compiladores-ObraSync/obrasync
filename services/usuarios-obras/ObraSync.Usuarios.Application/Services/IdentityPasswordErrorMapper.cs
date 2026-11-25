using Microsoft.AspNetCore.Identity;
using ObraSync.Usuarios.Domain.Exceptions;

namespace ObraSync.Usuarios.Application.Services;

internal static class IdentityPasswordErrorMapper
{
    public const string MensajePasswordCorta =
        "La contraseña debe tener al menos 8 caracteres.";

    public const string MensajePasswordSinDigito =
        "La contraseña debe incluir al menos un número.";

    public static void LanzarValidacionSiErrorDePassword(IdentityResult result)
    {
        if (result.Errors.Any(e => e.Code.Contains("PasswordTooShort", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ValidacionException(MensajePasswordCorta);
        }

        if (result.Errors.Any(e => e.Code.Contains("PasswordRequiresDigit", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ValidacionException(MensajePasswordSinDigito);
        }
    }
}
