using System.Text;

namespace ObraSync.Recepcion.Domain.Recepciones;

/// <summary>
/// Valida fotos de evidencia de entregas con problema (#217):
/// máximo 3 fotos, hasta 2 MB cada una, y verificación estricta del formato binario (JPEG, PNG o WebP)
/// mediante firmas mágicas (magic bytes), nunca confiando solo en la extensión o el header Content-Type.
/// </summary>
public static class ValidadorFotoEvidencia
{
    public const int MaxFotosPorRecepcion = 3;
    public const int MaxTamanoFotoBytes = 2 * 1024 * 1024; // 2 MB
    public const long MaxTamanoSolicitudMultipartBytes = 8 * 1024 * 1024; // 8 MB (3 x 2 MB + overhead multipart)
    public const int MaxLongitudNombreArchivo = 255;

    public const string ContentTypeJpeg = "image/jpeg";
    public const string ContentTypePng = "image/png";
    public const string ContentTypeWebp = "image/webp";

    /// <summary>
    /// Detecta el tipo MIME real a partir de los primeros bytes del archivo (magic bytes).
    /// Devuelve null si el contenido no corresponde a un JPEG, PNG o WebP válido.
    /// </summary>
    public static string? DetectarTipoContenido(ReadOnlySpan<byte> contenido)
    {
        // JPEG: FF D8 FF
        if (contenido.Length >= 3 &&
            contenido[0] == 0xFF &&
            contenido[1] == 0xD8 &&
            contenido[2] == 0xFF)
        {
            return ContentTypeJpeg;
        }

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (contenido.Length >= 8 &&
            contenido[0] == 0x89 &&
            contenido[1] == 0x50 &&
            contenido[2] == 0x4E &&
            contenido[3] == 0x47 &&
            contenido[4] == 0x0D &&
            contenido[5] == 0x0A &&
            contenido[6] == 0x1A &&
            contenido[7] == 0x0A)
        {
            return ContentTypePng;
        }

        // WebP: "RIFF" (0..3) + tamaño (4..7) + "WEBP" (8..11)
        if (contenido.Length >= 12 &&
            contenido[0] == 0x52 && // R
            contenido[1] == 0x49 && // I
            contenido[2] == 0x46 && // F
            contenido[3] == 0x46 && // F
            contenido[8] == 0x57 && // W
            contenido[9] == 0x45 && // E
            contenido[10] == 0x42 && // B
            contenido[11] == 0x50)   // P
        {
            return ContentTypeWebp;
        }

        return null;
    }

    /// <summary>
    /// Valida tamaño y contenido binario de una foto y devuelve el tipo MIME detectado y el nombre saneado.
    /// Lanza ArgumentException si la foto incumple alguna restricción.
    /// </summary>
    public static (string ContentType, string NombreArchivoSaneado) Validar(byte[]? contenido, string? nombreArchivo)
    {
        if (contenido is null || contenido.Length == 0)
        {
            throw new ArgumentException("La foto de evidencia está vacía.");
        }

        if (contenido.Length > MaxTamanoFotoBytes)
        {
            throw new ArgumentException("Cada foto de evidencia no puede superar los 2 MB.");
        }

        var contentType = DetectarTipoContenido(contenido);
        if (contentType is null)
        {
            throw new ArgumentException("Formato de imagen no válido. Solo se permiten imágenes JPEG, PNG o WebP válidas.");
        }

        var nombreSaneado = SanearNombreArchivo(nombreArchivo, contentType);
        return (contentType, nombreSaneado);
    }

    public static string SanearNombreArchivo(string? nombreArchivo, string contentType)
    {
        var extensionPorDefecto = contentType switch
        {
            ContentTypeJpeg => ".jpg",
            ContentTypePng => ".png",
            ContentTypeWebp => ".webp",
            _ => ".bin"
        };

        if (string.IsNullOrWhiteSpace(nombreArchivo))
        {
            return $"evidencia{extensionPorDefecto}";
        }

        var soloNombre = Path.GetFileName(nombreArchivo.Replace('\\', '/')).Trim();
        var sb = new StringBuilder(soloNombre.Length);
        foreach (char c in soloNombre)
        {
            if (!char.IsControl(c) && c != '/' && c != '\\')
            {
                sb.Append(c);
            }
        }

        var limpio = sb.ToString().Trim();
        if (string.IsNullOrWhiteSpace(limpio))
        {
            return $"evidencia{extensionPorDefecto}";
        }

        if (limpio.Length > MaxLongitudNombreArchivo)
        {
            limpio = limpio[..MaxLongitudNombreArchivo];
        }

        return limpio;
    }
}
