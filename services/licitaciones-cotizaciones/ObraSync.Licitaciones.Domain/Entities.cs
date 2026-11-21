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

    private static DateTime NormalizarAUtc(DateTime fecha) => fecha.Kind switch
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