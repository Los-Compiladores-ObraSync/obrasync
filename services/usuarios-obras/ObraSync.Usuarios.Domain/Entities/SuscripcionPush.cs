using System.Security.Cryptography;
using System.Text;
using ObraSync.Usuarios.Domain.Exceptions;

namespace ObraSync.Usuarios.Domain.Entities;

public class SuscripcionPush
{
    public const int MaxSuscripcionesPorUsuario = 5;
    public const int MaxLongitudEndpoint = 1000;
    public const int MaxLongitudEndpointHash = 64;
    public const int MaxLongitudClave = 500;
    public const int MaxLongitudNavegador = 200;

    public Guid Id { get; set; }
    public Guid UsuarioId { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string EndpointHash { get; set; } = string.Empty;
    public string P256dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;
    public string? Navegador { get; set; }
    public DateTime FechaAlta { get; set; } = DateTime.UtcNow;
    public DateTime? UltimoEnvio { get; set; }

    public AppUser? Usuario { get; set; }

    public static SuscripcionPush Crear(
        Guid usuarioId,
        string? endpoint,
        string? p256dh,
        string? auth,
        string? navegador,
        bool permitirHttpLocalhost = true,
        DateTime? fechaAltaUtc = null)
    {
        if (usuarioId == Guid.Empty)
        {
            throw new ValidacionException("El identificador del usuario autenticado es obligatorio.");
        }

        var endpointLimpio = ValidarYNormalizarEndpoint(endpoint, permitirHttpLocalhost);
        var p256dhLimpio = ValidarClave(p256dh, "p256dh");
        var authLimpio = ValidarClave(auth, "auth");
        var navegadorLimpio = NormalizarNavegador(navegador);
        var fechaUtc = NormalizarAUtc(fechaAltaUtc ?? DateTime.UtcNow);

        return new SuscripcionPush
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            Endpoint = endpointLimpio,
            EndpointHash = CalcularEndpointHash(endpointLimpio),
            P256dh = p256dhLimpio,
            Auth = authLimpio,
            Navegador = navegadorLimpio,
            FechaAlta = fechaUtc,
            UltimoEnvio = null
        };
    }

    public void Actualizar(
        string? p256dh,
        string? auth,
        string? navegador,
        DateTime? fechaActualizacionUtc = null)
    {
        P256dh = ValidarClave(p256dh, "p256dh");
        Auth = ValidarClave(auth, "auth");
        if (!string.IsNullOrWhiteSpace(navegador))
        {
            Navegador = NormalizarNavegador(navegador);
        }

        FechaAlta = NormalizarAUtc(fechaActualizacionUtc ?? DateTime.UtcNow);
    }

    public static string ValidarYNormalizarEndpoint(string? endpoint, bool permitirHttpLocalhost = true)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ValidacionException("El campo 'endpoint' de la suscripción push es obligatorio.");
        }

        var limpio = endpoint.Trim();
        if (limpio.Length > MaxLongitudEndpoint)
        {
            throw new ValidacionException($"El endpoint no puede superar los {MaxLongitudEndpoint} caracteres.");
        }

        if (!Uri.TryCreate(limpio, UriKind.Absolute, out var uri))
        {
            throw new ValidacionException("El campo 'endpoint' debe ser una URL absoluta válida.");
        }

        var esHttps = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        var esHttpLocal = permitirHttpLocalhost
            && string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase));

        if (!esHttps && !esHttpLocal)
        {
            throw new ValidacionException("El endpoint de suscripción push debe utilizar el protocolo HTTPS.");
        }

        return limpio;
    }

    public static string CalcularEndpointHash(string endpointNormalizado)
    {
        var bytes = Encoding.UTF8.GetBytes(endpointNormalizado);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public static string? TruncarNavegador(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return null;
        }

        var limpio = valor.Trim();
        return limpio.Length <= MaxLongitudNavegador
            ? limpio
            : limpio[..MaxLongitudNavegador];
    }

    private static string ValidarClave(string? valor, string nombreCampo)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            throw new ValidacionException($"La clave '{nombreCampo}' de la suscripción push es obligatoria.");
        }

        var limpio = valor.Trim();
        if (limpio.Length > MaxLongitudClave)
        {
            throw new ValidacionException($"La clave '{nombreCampo}' no puede superar los {MaxLongitudClave} caracteres.");
        }

        return limpio;
    }

    private static string? NormalizarNavegador(string? navegador)
    {
        if (string.IsNullOrWhiteSpace(navegador))
        {
            return null;
        }

        var limpio = navegador.Trim();
        if (limpio.Length > MaxLongitudNavegador)
        {
            throw new ValidacionException($"El campo 'navegador' no puede superar los {MaxLongitudNavegador} caracteres.");
        }

        return limpio;
    }

    private static DateTime NormalizarAUtc(DateTime fecha) => fecha.Kind switch
    {
        DateTimeKind.Utc => fecha,
        DateTimeKind.Local => fecha.ToUniversalTime(),
        _ => DateTime.SpecifyKind(fecha, DateTimeKind.Utc)
    };
}
