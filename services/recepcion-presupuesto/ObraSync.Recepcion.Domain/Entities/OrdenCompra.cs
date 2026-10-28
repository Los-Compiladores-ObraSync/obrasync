using ObraSync.Recepcion.Domain.Common;

namespace ObraSync.Recepcion.Domain.Entities;

public class OrdenCompra : BaseEntity
{
    // Identificador de la orden de compra emitida (Guid del evento, ValueGeneratedNever)
    public Guid ObraId { get; set; }
    public Guid ProveedorId { get; set; }
    public decimal MontoTotal { get; set; }
    public string? Etapa { get; set; }

    private readonly List<OrdenCompraItem> _items = new();
    public IReadOnlyCollection<OrdenCompraItem> Items => _items.AsReadOnly();

    // Constructor privado para EF Core
    private OrdenCompra() { }

    public static OrdenCompra Crear(
        Guid id,
        Guid obraId,
        Guid proveedorId,
        decimal montoTotal,
        IEnumerable<OrdenCompraItem> items,
        string? etapa = null)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("El identificador de la orden de compra no puede estar vacío.", nameof(id));

        if (obraId == Guid.Empty)
            throw new ArgumentException("El identificador de la obra no puede estar vacío.", nameof(obraId));

        if (proveedorId == Guid.Empty)
            throw new ArgumentException("El identificador del proveedor no puede estar vacío.", nameof(proveedorId));

        if (montoTotal < 0)
            throw new ArgumentException("El monto total no puede ser negativo.", nameof(montoTotal));

        if (items is null)
            throw new ArgumentNullException(nameof(items), "La colección de ítems no puede ser nula.");

        var listaItems = items.ToList();
        if (listaItems.Count == 0)
            throw new ArgumentException("La orden de compra debe contener al menos un ítem.", nameof(items));

        // Regla provisoria: se rechazan ítems duplicados por ArticuloId dentro de la misma OC
        var duplicados = listaItems
            .GroupBy(i => i.ArticuloId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicados.Count > 0)
            throw new ArgumentException($"La orden de compra contiene artículos duplicados: {string.Join(", ", duplicados)}.", nameof(items));

        if (!string.IsNullOrWhiteSpace(etapa) && etapa.Trim().Length > 200)
            throw new ArgumentException("La etapa no puede superar los 200 caracteres.", nameof(etapa));

        var oc = new OrdenCompra
        {
            Id = id,
            ObraId = obraId,
            ProveedorId = proveedorId,
            MontoTotal = montoTotal,
            Etapa = string.IsNullOrWhiteSpace(etapa) ? null : etapa.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        foreach (var item in listaItems)
        {
            item.OrdenCompraId = id;
            oc._items.Add(item);
        }

        return oc;
    }
}
