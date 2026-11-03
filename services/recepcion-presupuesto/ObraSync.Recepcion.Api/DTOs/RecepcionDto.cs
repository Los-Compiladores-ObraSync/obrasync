using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Domain.Enums;

namespace ObraSync.Recepcion.Api.DTOs;

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

    public static RecepcionDto DesdeEntidad(ObraSync.Recepcion.Domain.Entities.Recepcion entidad)
    {
        return new RecepcionDto
        {
            Id = entidad.Id,
            ObraId = entidad.ObraId,
            OrdenCompraId = entidad.OrdenCompraId,
            RecepcionadoPorUsuarioId = entidad.RecepcionadoPorUsuarioId,
            NumeroRemito = entidad.NumeroRemito,
            FechaRecepcion = ToUtc(entidad.FechaRecepcion),
            Estado = entidad.Estado,
            Observaciones = entidad.Observaciones,
            CreatedAt = ToUtc(entidad.CreatedAt),
            UpdatedAt = entidad.UpdatedAt.HasValue ? ToUtc(entidad.UpdatedAt.Value) : null,
            Detalles = entidad.Detalles.Select(DetalleRecepcionDto.DesdeEntidad).ToList()
        };
    }

    private static DateTime ToUtc(DateTime valor)
    {
        return valor.Kind switch
        {
            DateTimeKind.Unspecified => DateTime.SpecifyKind(valor, DateTimeKind.Utc),
            DateTimeKind.Local => valor.ToUniversalTime(),
            _ => valor
        };
    }
}
