using System.ComponentModel.DataAnnotations;

namespace ObraSync.Licitaciones.Domain.Entities;

public class RequisicionAprobada
{
    public Guid RequisicionId { get; set; }            // clave: el mismo Id que manda Requisiciones
    public string NumeroRequisicion { get; set; } = "";
    public Guid ObraId { get; set; }
    public string? Etapa { get; set; }                 // texto opcional que manda Requisiciones (#107)
    public DateTime RecibidaEnUtc { get; set; }
    public List<RequisicionAprobadaItem> Items { get; set; } = new();
}

public class RequisicionAprobadaItem
{
    public Guid Id { get; set; }
    public Guid RequisicionId { get; set; }
    public Guid ArticuloId { get; set; }
    public string Descripcion { get; set; } = "";
    public decimal Cantidad { get; set; }
    public string UnidadMedida { get; set; } = "";
}

public enum EstadoLicitacion
{
    Abierta,
    Cerrada,
    Adjudicada
}

public class Licitacion
{
    public Guid LicitacionId { get; set; }
    public Guid RequisicionId { get; set; }             // GUID, sin clave foránea
    public string NumeroRequisicion { get; set; } = "";
    public Guid ObraId { get; set; }
    public EstadoLicitacion Estado { get; set; }
    public DateTime FechaCreacion { get; set; }
    public DateTime FechaLimiteCotizacion { get; set; }
    public string? MotivoCierre { get; set; }
    public DateTime? FechaCierre { get; set; }
    public List<LicitacionItem> Items { get; set; } = new();

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

public class LicitacionItem
{
    public Guid Id { get; set; }
    public Guid LicitacionId { get; set; }
    public Guid ArticuloId { get; set; }
    public string Descripcion { get; set; } = "";
    public decimal Cantidad { get; set; }
    public string UnidadMedida { get; set; } = "";
}

public class Cotizacion
{
    public Guid CotizacionId { get; set; }
    public Guid LicitacionId { get; set; }
    public Guid ProveedorId { get; set; }
    /// <summary>Nombre del proveedor tal como figuraba al cotizar (#206). Null si no se conoce: no se inventa.</summary>
    public string? ProveedorNombre { get; set; }
    public decimal Flete { get; set; }
    public int PlazoEntregaDias { get; set; }
    public DateTime FechaCreacion { get; set; }
    public List<CotizacionItem> Items { get; set; } = new();
}

public class CotizacionItem
{
    public Guid Id { get; set; }
    public Guid CotizacionId { get; set; }
    public Guid ArticuloId { get; set; }
    public decimal PrecioUnitario { get; set; }
    public string? PresentacionOfrecida { get; set; }
    public decimal? CantidadOfrecida { get; set; }
    public bool CoberturaParcial { get; set; }

    public decimal CalcularSubtotal(decimal cantidadPliego) =>
        (CantidadOfrecida is > 0 ? CantidadOfrecida.Value : cantidadPliego) * PrecioUnitario;
}

public class OrdenCompra
{
    public const int MaxLongitudObservacionDespacho = 200;
    public const int MaxLongitudMotivoProblema = 200;

    public Guid OrdenCompraId { get; set; }
    public Guid LicitacionId { get; set; }
    public Guid CotizacionId { get; set; }
    public Guid ProveedorId { get; set; }
    public string Estado { get; set; } = "Emitida";
    public DateTime FechaEmision { get; set; } = DateTime.UtcNow;
    public DateTime? FechaDespacho { get; set; }
    public Guid? DespachadaPor { get; set; }
    public DateTime? FechaEstimadaLlegada { get; set; }
    public string? Observacion { get; set; }
    public bool RecibidaCompleta { get; set; }
    public DateTime? FechaRecepcionCompleta { get; set; }
    public bool TieneProblemaAbierto { get; set; }
    public string? MotivoProblemaAbierto { get; set; }
    public DateTime? FechaProblemaAbierto { get; set; }
    public DateTime? ProblemaResueltoEn { get; set; }
    public Guid? ProblemaResueltoPor { get; set; }
    public decimal TotalFlete { get; set; }
    public List<OrdenCompraItem> Items { get; set; } = new();

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public Guid ObraId { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public DateTime? DespachadaEn
    {
        get => FechaDespacho;
        set => FechaDespacho = value;
    }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool FueDespachada =>
        string.Equals(Estado, "Despachada", StringComparison.OrdinalIgnoreCase) || FechaDespacho.HasValue;

    public bool Despachar(
        Guid despachadaPor,
        DateTime? fechaDespachoUtc = null,
        DateTime? fechaEstimadaLlegada = null,
        string? observacion = null)
    {
        if (despachadaPor == Guid.Empty)
        {
            throw new ArgumentException("El identificador del usuario que despacha es obligatorio.", nameof(despachadaPor));
        }

        // Idempotencia primero (#216): si ya estaba despachada, no falla ni pisa los datos originales.
        if (FueDespachada)
        {
            return false;
        }

        var instante = NormalizarAUtc(fechaDespachoUtc ?? DateTime.UtcNow);

        DateTime? fechaEstimadaUtc = null;
        if (fechaEstimadaLlegada.HasValue)
        {
            fechaEstimadaUtc = NormalizarAUtc(fechaEstimadaLlegada.Value);
            if (fechaEstimadaUtc.Value.Date < instante.Date)
            {
                throw new ArgumentException(
                    "La fecha estimada de llegada no puede ser anterior a hoy.",
                    nameof(fechaEstimadaLlegada));
            }
        }

        string? observacionNormalizada = null;
        if (!string.IsNullOrWhiteSpace(observacion))
        {
            observacionNormalizada = observacion.Trim();
            if (observacionNormalizada.Length > MaxLongitudObservacionDespacho)
            {
                throw new ArgumentException(
                    $"La observación no puede superar los {MaxLongitudObservacionDespacho} caracteres.",
                    nameof(observacion));
            }
        }

        Estado = "Despachada";
        FechaDespacho = instante;
        DespachadaPor = despachadaPor;
        FechaEstimadaLlegada = fechaEstimadaUtc;
        Observacion = observacionNormalizada;
        return true;
    }

    public bool MarcarComoRecibidaCompleta(DateTime? fechaRecepcionUtc = null)
    {
        if (RecibidaCompleta)
        {
            return false;
        }

        FechaRecepcionCompleta = NormalizarAUtc(fechaRecepcionUtc ?? DateTime.UtcNow);
        RecibidaCompleta = true;
        return true;
    }

    public void AbrirProblema(string motivo, DateTime? fechaUtc = null)
    {
        if (string.IsNullOrWhiteSpace(motivo))
        {
            throw new ArgumentException("El motivo del problema es obligatorio.", nameof(motivo));
        }

        var motivoNormalizado = motivo.Trim();
        if (motivoNormalizado.Length > MaxLongitudMotivoProblema)
        {
            motivoNormalizado = motivoNormalizado[..MaxLongitudMotivoProblema];
        }

        TieneProblemaAbierto = true;
        MotivoProblemaAbierto = motivoNormalizado;
        FechaProblemaAbierto = NormalizarAUtc(fechaUtc ?? DateTime.UtcNow);
        ProblemaResueltoEn = null;
        ProblemaResueltoPor = null;
    }

    public bool ResolverProblema(DateTime? fechaUtc = null, Guid? resueltoPor = null)
    {
        if (!TieneProblemaAbierto)
        {
            return false;
        }

        TieneProblemaAbierto = false;
        ProblemaResueltoEn = NormalizarAUtc(fechaUtc ?? DateTime.UtcNow);
        ProblemaResueltoPor = resueltoPor is Guid uid && uid != Guid.Empty ? uid : null;
        return true;
    }

    internal static DateTime NormalizarAUtc(DateTime fecha) => fecha.Kind switch
    {
        DateTimeKind.Utc => fecha,
        DateTimeKind.Local => fecha.ToUniversalTime(),
        _ => DateTime.SpecifyKind(fecha, DateTimeKind.Utc)
    };
}

public class OrdenCompraItem
{
    public Guid Id { get; set; }
    public Guid OrdenCompraId { get; set; }
    public Guid ArticuloId { get; set; }
    public string Descripcion { get; set; } = "";
    public decimal Cantidad { get; set; }
    public string UnidadMedida { get; set; } = "";
    public decimal PrecioUnitario { get; set; }
}

public class MagicLink
{
    public Guid Id { get; set; }
    public string Hash { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string TokenHash
    {
        get => Hash;
        set => Hash = value;
    }

    public Guid LicitacionId { get; set; }
    public Guid ProveedorId { get; set; }
    /// <summary>
    /// Nombre del proveedor que escribió quien generó el enlace (#206). El Magic Link no tiene JWT, así que es la única fuente
    /// del nombre cuando el proveedor cotiza por este camino. Opcional.
    /// </summary>
    public string? ProveedorNombre { get; set; }
    public DateTime FechaDeVencimiento { get; set; }
    public DateTime? FechaDeUso { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

public class CalificacionProveedor
{
    public const int MaxLongitudComentario = 300;

    public Guid Id { get; set; }
    public Guid ProveedorId { get; set; }
    /// <summary>Orden de compra asociada a esta calificación (#222). Null para calificaciones legacy directas por proveedor (#88).</summary>
    public Guid? OrdenCompraId { get; set; }
    /// <summary>Usuario (sub del JWT) que calificó. Cada usuario tiene una sola calificación por orden (#222) o global (#88).</summary>
    public Guid CalificadorId { get; set; }
    public int Calificacion { get; set; }
    /// <summary>Comentario opcional del calificador sobre el cumplimiento en la orden (máx. 300 caracteres, #222).</summary>
    public string? Comentario { get; set; }
    public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;
}

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

public class LecturaChatOrden
{
    public Guid Id { get; set; }
    public Guid OrdenCompraId { get; set; }
    public Guid UsuarioId { get; set; }
    public DateTime UltimaLecturaEn { get; set; } = DateTime.UtcNow;
}