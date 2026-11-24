using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using ObraSync.Recepcion.Domain.Constants;

namespace ObraSync.Recepcion.Application.DTOs;

/// <summary>
/// Resumen de una orden de compra con datos de despacho, saldos por material
/// e historial cronológico de recepciones y problemas con sus fotos (#217).
/// </summary>
public class RecepcionesDeOrdenDto
{
    public Guid OrdenCompraId { get; set; }
    public Guid ObraId { get; set; }

    [JsonIgnore]
    public Guid ProveedorId { get; set; }

    public string? Etapa { get; set; }
    public DateTime? FechaDespacho { get; set; }
    public DateTime? FechaEstimadaLlegada { get; set; }
    public string? ObservacionDespacho { get; set; }
    public bool EstaCompleta { get; set; }
    public bool TieneProblemaAbierto { get; set; }
    public bool EsperaNuevoDespacho { get; set; }
    public bool BloqueadaPorPresupuesto { get; set; }
    public string EstadoEntrega { get; set; } = EstadosEntregaOrden.Pendiente;
    public List<ItemOrdenPendienteDto> Items { get; set; } = new();
    public List<RecepcionDto> Recepciones { get; set; } = new();
}
