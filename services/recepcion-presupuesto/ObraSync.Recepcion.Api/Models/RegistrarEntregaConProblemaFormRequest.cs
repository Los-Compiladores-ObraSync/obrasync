using Microsoft.AspNetCore.Http;

namespace ObraSync.Recepcion.Api.Models;

/// <summary>
/// Formulario multipart/form-data para registrar una entrega con problema y hasta 3 fotos de evidencia (#217).
/// </summary>
public class RegistrarEntregaConProblemaFormRequest
{
    public Guid ObraId { get; set; }
    public Guid OrdenCompraId { get; set; }
    public string Motivo { get; set; } = string.Empty;
    public string? Detalle { get; set; }
    public string? NumeroRemito { get; set; }
    public DateTime? FechaRecepcion { get; set; }
    public List<IFormFile>? Fotos { get; set; }
}
