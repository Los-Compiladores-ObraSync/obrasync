using System;
using System.Collections.Generic;
using ObraSync.Recepcion.Domain.Constants;

namespace ObraSync.Recepcion.Application.DTOs;

public class OrdenPendienteDto
{
    public Guid OrdenCompraId { get; set; }
    public Guid ObraId { get; set; }
    public string? Etapa { get; set; }
    public bool SinPartidaPresupuestaria { get; set; }
    public DateTime? FechaDespacho { get; set; }
    public DateTime? FechaEstimadaLlegada { get; set; }
    public string? ObservacionDespacho { get; set; }
    public bool TieneProblemaAbierto { get; set; }
    /// <summary>La entrega volvió al proveedor: el capataz no puede recibir hasta que despache de nuevo.</summary>
    public bool EsperaNuevoDespacho { get; set; }
    public string EstadoEntrega { get; set; } = EstadosEntregaOrden.Pendiente;
    public List<ItemOrdenPendienteDto> Items { get; set; } = new();
}
