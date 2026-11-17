using System;

namespace ObraSync.Recepcion.Application.DTOs;

public class OrdenBloqueadaDto
{
    public Guid OrdenCompraId { get; set; }
    public Guid ObraId { get; set; }
    public Guid? PartidaId { get; set; }
    public string? Etapa { get; set; }
    public decimal MontoOrdenCompra { get; set; }
    public decimal MontoAsignadoPartida { get; set; }
    public decimal MontoComprometidoActual { get; set; }
    public decimal MontoProyectado { get; set; }
    public decimal Exceso { get; set; }
    public string Motivo { get; set; } = string.Empty;
    public DateTime FechaBloqueo { get; set; }
}
