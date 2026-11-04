namespace ObraSync.Licitaciones.Application.DTOs;

public class CrearLicitacionRequestDto
{
    public Guid RequisicionId { get; set; }
    public DateTime FechaLimiteCotizacion { get; set; }
}

public class PliegoItemDto
{
    public Guid ArticuloId { get; set; }
    public string Descripcion { get; set; } = "";
    public decimal Cantidad { get; set; }
    public string UnidadMedida { get; set; } = "";
}

public class LicitacionResponseDto
{
    public Guid LicitacionId { get; set; }
    public Guid RequisicionId { get; set; }
    public string NumeroRequisicion { get; set; } = "";
    public Guid ObraId { get; set; }
    public string Estado { get; set; } = "";
    public DateTime FechaCreacion { get; set; }
    public DateTime FechaLimiteCotizacion { get; set; }
    public List<PliegoItemDto> Items { get; set; } = new();
}