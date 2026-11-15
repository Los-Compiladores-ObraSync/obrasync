using System;
using System.Collections.Generic;
using ObraSync.Recepcion.Application.Mappings;
using ObraSync.Recepcion.Domain.Enums;

namespace ObraSync.Recepcion.Application.DTOs;

public class RecepcionDto
{
    public Guid Id { get; set; }
    public Guid ObraId { get; set; }
    public Guid OrdenCompraId { get; set; }
    public Guid RecepcionadoPorUsuarioId { get; set; }
    public string NumeroRemito { get; set; } = string.Empty;
    public DateTime FechaRecepcion { get; set; }
    public EstadoRecepcion Estado { get; set; }
    public string? Observaciones { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public List<DetalleRecepcionDto> Detalles { get; set; } = new();

    /// <summary>
    /// Mapea desde la entidad de dominio delegando al método de extensión ToDto() en Application.Mappings.
    /// Mantenido por compatibilidad hacia atrás con tests y código existente.
    /// </summary>
    public static RecepcionDto DesdeEntidad(ObraSync.Recepcion.Domain.Entities.Recepcion entidad)
    {
        return entidad.ToDto();
    }
}
