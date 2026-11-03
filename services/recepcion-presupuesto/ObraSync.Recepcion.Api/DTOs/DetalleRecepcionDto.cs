using ObraSync.Recepcion.Domain.Entities;

namespace ObraSync.Recepcion.Api.DTOs;

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

    public static DetalleRecepcionDto DesdeEntidad(ObraSync.Recepcion.Domain.Entities.DetalleRecepcion entidad)
    {
        return new DetalleRecepcionDto
        {
            Id = entidad.Id,
            RecepcionId = entidad.RecepcionId,
            ArticuloId = entidad.ArticuloId,
            DescripcionArticulo = entidad.DescripcionArticulo,
            CantidadEsperada = entidad.CantidadEsperada,
            CantidadRecibida = entidad.CantidadRecibida,
            UnidadMedida = entidad.UnidadMedida,
            Conforme = entidad.Conforme,
            Observacion = entidad.Observacion,
            CreatedAt = ToUtc(entidad.CreatedAt),
            UpdatedAt = entidad.UpdatedAt.HasValue ? ToUtc(entidad.UpdatedAt.Value) : null
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
