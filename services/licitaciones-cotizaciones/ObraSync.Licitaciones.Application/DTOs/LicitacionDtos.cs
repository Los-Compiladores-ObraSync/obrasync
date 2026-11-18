namespace ObraSync.Licitaciones.Application.DTOs;

public class CrearLicitacionRequestDto
{
    public Guid RequisicionId { get; set; }
    public DateTime FechaLimiteCotizacion { get; set; }
}

public class CerrarLicitacionRequestDto
{
    public string? Motivo { get; set; }
}
