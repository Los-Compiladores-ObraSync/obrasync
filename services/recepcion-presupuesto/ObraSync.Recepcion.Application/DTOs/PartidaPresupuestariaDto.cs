using System;

namespace ObraSync.Recepcion.Application.DTOs;

public class PartidaPresupuestariaDto
{
    public Guid Id { get; set; }
    public Guid ObraId { get; set; }
    public string? Etapa { get; set; }
    public string CodigoPartida { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public decimal MontoAsignado { get; set; }
    public decimal MontoComprometido { get; set; }
    public decimal MontoEjecutado { get; set; }
    public decimal MontoDisponible { get; set; }
    public string Moneda { get; set; } = "ARS";
    public DateTime CreatedAt { get; set; }
}
