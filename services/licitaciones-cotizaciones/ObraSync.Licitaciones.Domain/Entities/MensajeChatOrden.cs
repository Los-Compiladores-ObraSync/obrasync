namespace ObraSync.Licitaciones.Domain.Entities;

public class MensajeChatOrden
{
    public const int MaxLongitudTexto = 1000;
    public const int MaxLongitudFragmento = 120;
    public const int MaxLongitudAutorNombre = 200;
    public const int MaxLongitudAutorRol = 30;

    public Guid Id { get; set; }
    public Guid OrdenCompraId { get; set; }
    public Guid? AutorId { get; set; }
    public string AutorNombre { get; set; } = string.Empty;
    public string AutorRol { get; set; } = string.Empty;
    public bool EsSistema { get; set; }
    public string Texto { get; set; } = string.Empty;
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public static MensajeChatOrden CrearDePersona(
        Guid ordenCompraId,
        Guid autorId,
        string? autorNombre,
        string autorRol,
        string? texto,
        DateTime? fechaCreacionUtc = null)
    {
        if (ordenCompraId == Guid.Empty)
        {
            throw new ArgumentException("El identificador de la orden de compra es obligatorio.", nameof(ordenCompraId));
        }

        if (autorId == Guid.Empty)
        {
            throw new ArgumentException("El identificador del usuario remitente es obligatorio.", nameof(autorId));
        }

        if (string.IsNullOrWhiteSpace(autorRol))
        {
            throw new ArgumentException("El rol del remitente es obligatorio.", nameof(autorRol));
        }

        if (string.IsNullOrWhiteSpace(texto))
        {
            throw new ArgumentException("El texto del mensaje es obligatorio.", nameof(texto));
        }

        var textoLimpio = texto.Trim();
        if (textoLimpio.Length > MaxLongitudTexto)
        {
            throw new ArgumentException(
                $"El mensaje no puede superar los {MaxLongitudTexto} caracteres.",
                nameof(texto));
        }

        var rolLimpio = autorRol.Trim();
        if (rolLimpio.Length > MaxLongitudAutorRol)
        {
            rolLimpio = rolLimpio[..MaxLongitudAutorRol];
        }

        var nombreLimpio = string.IsNullOrWhiteSpace(autorNombre) ? rolLimpio : autorNombre.Trim();
        if (nombreLimpio.Length > MaxLongitudAutorNombre)
        {
            nombreLimpio = nombreLimpio[..MaxLongitudAutorNombre];
        }

        return new MensajeChatOrden
        {
            Id = Guid.NewGuid(),
            OrdenCompraId = ordenCompraId,
            AutorId = autorId,
            AutorNombre = nombreLimpio,
            AutorRol = rolLimpio,
            EsSistema = false,
            Texto = textoLimpio,
            FechaCreacion = OrdenCompra.NormalizarAUtc(fechaCreacionUtc ?? DateTime.UtcNow)
        };
    }

    public static MensajeChatOrden CrearDelSistema(
        Guid ordenCompraId,
        string texto,
        DateTime? fechaCreacionUtc = null)
    {
        if (ordenCompraId == Guid.Empty)
        {
            throw new ArgumentException("El identificador de la orden de compra es obligatorio.", nameof(ordenCompraId));
        }

        if (string.IsNullOrWhiteSpace(texto))
        {
            throw new ArgumentException("El texto del mensaje del sistema es obligatorio.", nameof(texto));
        }

        var textoLimpio = texto.Trim();
        if (textoLimpio.Length > MaxLongitudTexto)
        {
            textoLimpio = textoLimpio[..MaxLongitudTexto];
        }

        return new MensajeChatOrden
        {
            Id = Guid.NewGuid(),
            OrdenCompraId = ordenCompraId,
            AutorId = null,
            AutorNombre = "Sistema",
            AutorRol = "Sistema",
            EsSistema = true,
            Texto = textoLimpio,
            FechaCreacion = OrdenCompra.NormalizarAUtc(fechaCreacionUtc ?? DateTime.UtcNow)
        };
    }

    public string ObtenerFragmento()
    {
        var textoLimpio = Texto.Trim();
        if (textoLimpio.Length <= MaxLongitudFragmento)
        {
            return textoLimpio;
        }

        return string.Concat(textoLimpio.AsSpan(0, MaxLongitudFragmento - 1), "…");
    }
}
