using System.Text.Json.Serialization;
using ObraSync.Recepcion.Domain.Common;

namespace ObraSync.Recepcion.Domain.Entities;

public class OrdenCompraItem : BaseEntity
{
    public Guid OrdenCompraId { get; set; }

    [JsonIgnore]
    public OrdenCompra OrdenCompra { get; set; } = null!;

    public Guid ArticuloId { get; set; }
    public string? Descripcion { get; set; }
    public decimal Cantidad { get; set; }
    public string UnidadMedida { get; set; } = string.Empty;
    public decimal PrecioUnitario { get; set; }

    // Constructor privado para EF Core
    private OrdenCompraItem() { }

    public static OrdenCompraItem Crear(
        Guid articuloId,
        decimal cantidad,
        string unidadMedida,
        decimal precioUnitario,
        string? descripcion = null)
    {
        if (articuloId == Guid.Empty)
            throw new ArgumentException("El identificador del artículo no puede estar vacío.", nameof(articuloId));

        if (cantidad <= 0)
            throw new ArgumentException("La cantidad debe ser mayor a cero.", nameof(cantidad));

        if (string.IsNullOrWhiteSpace(unidadMedida))
            throw new ArgumentException("La unidad de medida es obligatoria.", nameof(unidadMedida));

        if (unidadMedida.Trim().Length > 50)
            throw new ArgumentException("La unidad de medida no puede superar los 50 caracteres.", nameof(unidadMedida));

        if (precioUnitario < 0)
            throw new ArgumentException("El precio unitario no puede ser negativo.", nameof(precioUnitario));

        return new OrdenCompraItem
        {
            Id = Guid.NewGuid(),
            ArticuloId = articuloId,
            Cantidad = cantidad,
            UnidadMedida = unidadMedida.Trim().ToUpperInvariant(),
            PrecioUnitario = precioUnitario,
            Descripcion = string.IsNullOrWhiteSpace(descripcion) ? null : descripcion.Trim(),
            CreatedAt = DateTime.UtcNow
        };
    }
}
