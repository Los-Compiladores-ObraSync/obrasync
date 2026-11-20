using MediatR;
using ObraSync.Licitaciones.Application.DTOs;

namespace ObraSync.Licitaciones.Application.MagicLinks.Queries.ObtenerPliegoPorMagicLink;

/// <summary>Pide el pliego al que da acceso un Magic Link, sin consumir el enlace (#158).</summary>
public record ObtenerPliegoPorMagicLinkQuery(string Hash) : IRequest<PliegoMagicLinkResponseDto>;

/// <summary>
/// Lo que ve el proveedor al abrir su enlace, antes de cotizar. Es una vista pública (no hay JWT), por eso lleva
/// solo lo necesario para cotizar: no incluye la obra ni la requisición.
/// </summary>
public class PliegoMagicLinkResponseDto
{
    public Guid LicitacionId { get; set; }
    public string NumeroRequisicion { get; set; } = string.Empty;
    public DateTime FechaLimiteCotizacion { get; set; }
    /// <summary>Hasta cuándo se puede usar el enlace (UTC).</summary>
    public DateTime FechaDeVencimientoEnlace { get; set; }
    /// <summary>Nombre que escribió Compras al generar el enlace; null si no puso ninguno.</summary>
    public string? ProveedorNombre { get; set; }
    public IReadOnlyList<PliegoItemDto> Items { get; set; } = Array.Empty<PliegoItemDto>();
}
