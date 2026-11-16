namespace ObraSync.Asistente.Api.Controllers;

/// <summary>
/// Reglas de la foto del remito (#209): JPEG, PNG o WEBP, con contenido y de hasta 5 MB.
/// El tipo se decide por los PRIMEROS BYTES del archivo, no por lo que diga el cliente (nombre o Content-Type).
/// </summary>
public static class ValidadorImagenRemito
{
    public const int MaxBytes = 5 * 1024 * 1024;

    /// <summary>Devuelve el tipo de medio ("image/jpeg", etc.) o, si no sirve, el motivo del rechazo.</summary>
    public static (string? TipoMedia, string? Error) Validar(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
            return (null, "La imagen está vacía.");
        if (bytes.Length > MaxBytes)
            return (null, "La imagen no puede pesar más de 5 MB.");

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return ("image/jpeg", null);

        ReadOnlySpan<byte> png = stackalloc byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        if (bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(png))
            return ("image/png", null);

        // WEBP: "RIFF" + 4 bytes de tamaño + "WEBP".
        if (bytes.Length >= 12
            && bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F'
            && bytes[8] == 'W' && bytes[9] == 'E' && bytes[10] == 'B' && bytes[11] == 'P')
            return ("image/webp", null);

        return (null, "La imagen debe ser JPG, PNG o WEBP.");
    }
}
