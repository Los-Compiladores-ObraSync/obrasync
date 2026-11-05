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
    public Guid RequisicionId { get; set; }            // GUID, sin clave foránea
    public string NumeroRequisicion { get; set; } = "";
    public Guid ObraId { get; set; }
    public EstadoLicitacion Estado { get; set; }
    public DateTime FechaCreacion { get; set; }
    public DateTime FechaLimiteCotizacion { get; set; }
    public List<LicitacionItem> Items { get; set; } = new();
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