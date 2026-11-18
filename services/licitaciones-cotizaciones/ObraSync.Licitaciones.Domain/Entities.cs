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
    public Guid OrdenCompraId { get; set; }
    public Guid LicitacionId { get; set; }
    public Guid CotizacionId { get; set; }
    public Guid ProveedorId { get; set; }
    public string Estado { get; set; } = "Emitida";
    public DateTime FechaEmision { get; set; } = DateTime.UtcNow;
    public decimal TotalFlete { get; set; }
    public List<OrdenCompraItem> Items { get; set; } = new();
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