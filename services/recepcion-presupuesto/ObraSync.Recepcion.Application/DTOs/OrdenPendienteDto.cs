using System;
using System.Collections.Generic;

namespace ObraSync.Recepcion.Application.DTOs;

public class OrdenPendienteDto
{
    public Guid OrdenCompraId { get; set; }
    public Guid ObraId { get; set; }
    public string? Etapa { get; set; }
    public bool SinPartidaPresupuestaria { get; set; }
    public List<ItemOrdenPendienteDto> Items { get; set; } = new();
}
