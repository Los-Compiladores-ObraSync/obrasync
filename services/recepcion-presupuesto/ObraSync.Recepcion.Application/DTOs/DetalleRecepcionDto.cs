using System;
using ObraSync.Recepcion.Application.Mappings;

namespace ObraSync.Recepcion.Application.DTOs;

public class DetalleRecepcionDto
{
    public Guid Id { get; set; }
    public Guid RecepcionId { get; set; }
    public Guid ArticuloId { get; set; }
    public string DescripcionArticulo { get; set; } = string.Empty;
    public decimal CantidadEsperada { get; set; }
    public decimal CantidadRecibida { get; set; }
    public string UnidadMedida { get; set; } = string.Empty;
    public bool Conforme { get; set; }
    public string? Observacion { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Mapea desde la entidad de dominio delegando al método de extensión ToDto() en Application.Mappings.
    /// Mantenido por compatibilidad hacia atrás con tests y código existente.
    /// </summary>
    public static DetalleRecepcionDto DesdeEntidad(ObraSync.Recepcion.Domain.Entities.DetalleRecepcion entidad)
    {
        return entidad.ToDto();
    }
}
