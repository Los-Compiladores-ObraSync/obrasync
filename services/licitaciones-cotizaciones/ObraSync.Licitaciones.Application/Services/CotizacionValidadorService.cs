using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.Mappings;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.Services;

public class CotizacionValidadorService : ICotizacionValidadorService
{
    private readonly ILicitacionRepository _licitacionRepository;
    private readonly ICotizacionRepository _cotizacionRepository;

    public CotizacionValidadorService(
        ILicitacionRepository licitacionRepository,
        ICotizacionRepository cotizacionRepository)
    {
        _licitacionRepository = licitacionRepository ?? throw new ArgumentNullException(nameof(licitacionRepository));
        _cotizacionRepository = cotizacionRepository ?? throw new ArgumentNullException(nameof(cotizacionRepository));
    }

    public async Task<(Licitacion Licitacion, Cotizacion Cotizacion, CotizacionResponseDto ResponseDto)> ValidarYConstruirAsync(
        Guid licitacionId,
        Guid proveedorId,
        decimal flete,
        int plazoEntregaDias,
        IReadOnlyList<CrearCotizacionItemDto>? items,
        CancellationToken cancellationToken = default,
        string? proveedorNombre = null)
    {
        if (licitacionId == Guid.Empty)
        {
            throw new ArgumentException("El identificador de la licitación es obligatorio.");
        }

        if (proveedorId == Guid.Empty)
        {
            throw new ArgumentException("El identificador del proveedor es obligatorio.");
        }

        if (flete < 0)
        {
            throw new ArgumentException("El flete no puede ser negativo.");
        }

        if (plazoEntregaDias <= 0)
        {
            throw new ArgumentException("El plazo de entrega en días debe ser mayor a cero.");
        }

        if (items == null || items.Count == 0)
        {
            throw new ArgumentException("La cotización debe incluir al menos un ítem.");
        }

        if (items.Any(i => i == null || i.ArticuloId == Guid.Empty || i.PrecioUnitario <= 0))
        {
            throw new ArgumentException("Todos los ítems cotizados deben tener un artículo válido y un precioUnitario mayor a cero.");
        }

        // 1. Verificar existencia de la licitación (404 Not Found)
        var licitacion = await _licitacionRepository.GetByIdAsync(licitacionId, cancellationToken);
        if (licitacion == null)
        {
            throw new KeyNotFoundException($"No se encontró la licitación con Id '{licitacionId}'.");
        }

        // 2. Validar que la licitación esté Abierta (400 Bad Request)
        if (licitacion.Estado != EstadoLicitacion.Abierta)
        {
            throw new ArgumentException($"No se pueden recibir cotizaciones porque la licitación se encuentra en estado '{licitacion.Estado}'.");
        }

        // 3. Validar que la fecha límite de cotización no haya vencido (400 Bad Request)
        var fechaLimiteUtc = DateTime.SpecifyKind(licitacion.FechaLimiteCotizacion, DateTimeKind.Utc);
        if (DateTime.UtcNow > fechaLimiteUtc)
        {
            throw new ArgumentException("El plazo límite para cotizar esta licitación ya venció.");
        }

        // 4. Resolver cada ítem cotizado contra el pliego (admite ArticuloId o LicitacionItem.Id)
        var itemsPorArticuloId = licitacion.Items
            .GroupBy(i => i.ArticuloId)
            .ToDictionary(g => g.Key, g => g.First());

        var itemsPorLicitacionItemId = licitacion.Items
            .Where(i => i.Id != Guid.Empty)
            .GroupBy(i => i.Id)
            .ToDictionary(g => g.Key, g => g.First());

        var articulosResueltos = new List<(Guid ArticuloId, decimal PrecioUnitario)>(items.Count);

        foreach (var item in items)
        {
            if (itemsPorArticuloId.TryGetValue(item.ArticuloId, out var pliegoItemPorArt))
            {
                articulosResueltos.Add((pliegoItemPorArt.ArticuloId, item.PrecioUnitario));
            }
            else if (itemsPorLicitacionItemId.TryGetValue(item.ArticuloId, out var pliegoItemPorId))
            {
                articulosResueltos.Add((pliegoItemPorId.ArticuloId, item.PrecioUnitario));
            }
            else
            {
                throw new ArgumentException("Los artículos cotizados deben coincidir exactamente con los artículos del pliego de la licitación (sin faltantes ni sobrantes).");
            }
        }

        var articulosPliego = itemsPorArticuloId.Keys.ToHashSet();
        var articulosCotizados = articulosResueltos.Select(x => x.ArticuloId).ToList();

        if (articulosCotizados.Count != articulosPliego.Count ||
            articulosCotizados.Distinct().Count() != articulosPliego.Count ||
            !articulosCotizados.All(articulosPliego.Contains))
        {
            throw new ArgumentException("Los artículos cotizados deben coincidir exactamente con los artículos del pliego de la licitación (sin faltantes ni sobrantes).");
        }

        // 5. Validar que el proveedor no haya cotizado previamente la misma licitación (409 Conflict)
        var yaCotizo = await _cotizacionRepository.ExistsByLicitacionAndProveedorAsync(
            licitacionId,
            proveedorId,
            cancellationToken);

        if (yaCotizo)
        {
            throw new InvalidOperationException("El proveedor ya presentó una cotización para esta licitación.");
        }

        var cotizacionId = Guid.NewGuid();
        var cotizacion = new Cotizacion
        {
            CotizacionId = cotizacionId,
            LicitacionId = licitacionId,
            ProveedorId = proveedorId,
            ProveedorNombre = NombreProveedor.Limpiar(proveedorNombre),
            Flete = flete,
            PlazoEntregaDias = plazoEntregaDias,
            FechaCreacion = DateTime.UtcNow,
            Items = articulosResueltos
                .Select(i => new CotizacionItem
                {
                    Id = Guid.NewGuid(),
                    CotizacionId = cotizacionId,
                    ArticuloId = i.ArticuloId,
                    PrecioUnitario = i.PrecioUnitario
                })
                .ToList()
        };

        decimal subtotalItems = cotizacion.Items.Sum(i =>
            itemsPorArticuloId.TryGetValue(i.ArticuloId, out var pliegoItem)
                ? pliegoItem.Cantidad * i.PrecioUnitario
                : 0m);

        var dto = cotizacion.ToDto();
        dto.SubtotalItems = subtotalItems;
        dto.CostoTotal = subtotalItems + cotizacion.Flete;

        return (licitacion, cotizacion, dto);
    }
}
