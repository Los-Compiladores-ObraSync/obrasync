using System.Globalization;
using System.Text.RegularExpressions;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.Mappings;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.Services;

public class CotizacionValidadorService : ICotizacionValidadorService
{
    private const int LongitudMaximaPresentacion = 100;

    private static readonly Regex MedidaUnitariaRegex = new(
        @"(\d+(?:[.,]\d+)?)\s*(kg|kilos?|gr|g|lts?|litros?|ml|m3|m2|mts?|metros?|cm)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ILicitacionRepository _licitacionRepository;
    private readonly ICotizacionRepository _cotizacionRepository;

    /// <summary>Tope del plazo de entrega ofrecido, en días corridos desde que se cotiza.</summary>
    public const int PlazoEntregaMaximoDias = 15;

    public CotizacionValidadorService(
        ILicitacionRepository licitacionRepository,
        ICotizacionRepository cotizacionRepository)
    {
        _licitacionRepository = licitacionRepository ?? throw new ArgumentNullException(nameof(licitacionRepository));
        _cotizacionRepository = cotizacionRepository ?? throw new ArgumentNullException(nameof(cotizacionRepository));
    }

    private static bool SeCuentaEnBolsas(string? unidadPedida, string? presentacionOfrecida) =>
        string.Equals(unidadPedida?.Trim(), "BOLSA", StringComparison.OrdinalIgnoreCase)
        || (presentacionOfrecida != null
            && System.Text.RegularExpressions.Regex.IsMatch(presentacionOfrecida, @"^\s*bolsas?\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase));

    public async Task<(Licitacion Licitacion, Cotizacion Cotizacion, CotizacionResponseDto ResponseDto)> ValidarYConstruirAsync(
        Guid licitacionId,
        Guid proveedorId,
        decimal flete,
        int plazoEntregaDias,
        IReadOnlyList<CrearCotizacionItemDto>? items,
        CancellationToken cancellationToken = default,
        string? proveedorNombre = null,
        bool permitirReemplazo = false)
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
            throw new ArgumentException("El plazo de entrega debe ser un número entero positivo expresado en días corridos (mayor a 0).");
        }

        if (plazoEntregaDias > PlazoEntregaMaximoDias)
        {
            throw new ArgumentException($"El plazo de entrega no puede superar los {PlazoEntregaMaximoDias} días, contados desde que se cotiza.");
        }

        if (items == null || items.Count == 0)
        {
            throw new ArgumentException("La cotización debe incluir al menos un ítem.");
        }

        if (items.Any(i => i == null || i.ArticuloId == Guid.Empty || i.PrecioUnitario <= 0))
        {
            throw new ArgumentException("Todos los ítems cotizados deben tener un artículo válido y un precioUnitario mayor a cero.");
        }

        foreach (var item in items)
        {
            if (item.CantidadOfrecida.HasValue && item.CantidadOfrecida.Value <= 0)
            {
                throw new ArgumentException("La cantidad ofrecida (cantidadOfrecida) debe ser mayor a cero.");
            }

            if (item.PresentacionOfrecida != null && item.PresentacionOfrecida.Trim().Length > LongitudMaximaPresentacion)
            {
                throw new ArgumentException($"La presentación ofrecida (presentacionOfrecida) no puede superar los {LongitudMaximaPresentacion} caracteres.");
            }
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

        var articulosResueltos = new List<(
            LicitacionItem PliegoItem,
            decimal PrecioUnitario,
            string? PresentacionOfrecida,
            decimal? CantidadOfrecida,
            bool CoberturaParcial)>(items.Count);

        foreach (var item in items)
        {
            LicitacionItem? pliegoItem = null;
            if (itemsPorArticuloId.TryGetValue(item.ArticuloId, out var pliegoItemPorArt))
            {
                pliegoItem = pliegoItemPorArt;
            }
            else if (itemsPorLicitacionItemId.TryGetValue(item.ArticuloId, out var pliegoItemPorId))
            {
                pliegoItem = pliegoItemPorId;
            }

            if (pliegoItem == null)
            {
                throw new ArgumentException("Los artículos cotizados deben coincidir exactamente con los artículos del pliego de la licitación (sin faltantes ni sobrantes).");
            }

            var presentacionLimpia = string.IsNullOrWhiteSpace(item.PresentacionOfrecida)
                ? null
                : item.PresentacionOfrecida.Trim();

            // Las bolsas se cuentan enteras: no existe "1.4 bolsas" (ni en el pliego ni en lo que ofrece el proveedor).
            if (item.CantidadOfrecida.HasValue
                && item.CantidadOfrecida.Value != decimal.Truncate(item.CantidadOfrecida.Value)
                && SeCuentaEnBolsas(pliegoItem.UnidadMedida, presentacionLimpia))
            {
                throw new ArgumentException("La cantidad ofrecida de bolsas debe ser un número entero (no se pueden ofrecer fracciones de bolsa).");
            }

            var coberturaParcial = EvaluarCoberturaParcial(
                pliegoItem,
                presentacionLimpia,
                item.CantidadOfrecida,
                item.CoberturaParcial);

            articulosResueltos.Add((
                pliegoItem,
                item.PrecioUnitario,
                presentacionLimpia,
                item.CantidadOfrecida,
                coberturaParcial));
        }

        var articulosPliego = itemsPorArticuloId.Keys.ToHashSet();
        var articulosCotizados = articulosResueltos.Select(x => x.PliegoItem.ArticuloId).ToList();

        if (articulosCotizados.Count != articulosPliego.Count ||
            articulosCotizados.Distinct().Count() != articulosPliego.Count ||
            !articulosCotizados.All(articulosPliego.Contains))
        {
            throw new ArgumentException("Los artículos cotizados deben coincidir exactamente con los artículos del pliego de la licitación (sin faltantes ni sobrantes).");
        }

        // 5. Validar que el proveedor no haya cotizado previamente la misma licitación (409 Conflict)
        // Al corregir una oferta propia (permitirReemplazo) ya existe una cotización del proveedor: es lo esperado.
        var yaCotizo = !permitirReemplazo && await _cotizacionRepository.ExistsByLicitacionAndProveedorAsync(
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
                    ArticuloId = i.PliegoItem.ArticuloId,
                    PrecioUnitario = i.PrecioUnitario,
                    PresentacionOfrecida = i.PresentacionOfrecida,
                    CantidadOfrecida = i.CantidadOfrecida,
                    CoberturaParcial = i.CoberturaParcial
                })
                .ToList()
        };

        var dto = cotizacion.ToDto();
        decimal subtotalItems = 0m;
        foreach (var itemDto in dto.Items)
        {
            var cantidadPliego = itemsPorArticuloId.TryGetValue(itemDto.ArticuloId, out var pliegoItem)
                ? pliegoItem.Cantidad
                : 0m;
            var cantidadEfectiva = itemDto.CantidadOfrecida is > 0 ? itemDto.CantidadOfrecida.Value : cantidadPliego;
            itemDto.SubtotalItem = cantidadEfectiva * itemDto.PrecioUnitario;
            subtotalItems += itemDto.SubtotalItem;
        }

        dto.SubtotalItems = subtotalItems;
        dto.CostoTotal = subtotalItems + cotizacion.Flete;
        dto.CoberturaParcial = cotizacion.Items.Any(i => i.CoberturaParcial);

        return (licitacion, cotizacion, dto);
    }

    public static bool EvaluarCoberturaParcial(
        LicitacionItem pliegoItem,
        string? presentacionOfrecida,
        decimal? cantidadOfrecida,
        bool coberturaParcialExplicita = false)
    {
        if (coberturaParcialExplicita)
        {
            return true;
        }

        var cantidadEfectiva = cantidadOfrecida ?? pliegoItem.Cantidad;

        if (!string.IsNullOrWhiteSpace(presentacionOfrecida) &&
            TryExtraerMagnitudNormalizada(pliegoItem.Descripcion, out var magPliego, out var familiaPliego) &&
            TryExtraerMagnitudNormalizada(presentacionOfrecida, out var magOferta, out var familiaOferta) &&
            string.Equals(familiaPliego, familiaOferta, StringComparison.OrdinalIgnoreCase))
        {
            var totalPedido = pliegoItem.Cantidad * magPliego;
            var totalOfrecido = cantidadEfectiva * magOferta;
            return totalOfrecido < totalPedido;
        }

        if (cantidadOfrecida.HasValue && cantidadOfrecida.Value < pliegoItem.Cantidad)
        {
            return true;
        }

        return false;
    }

    private static bool TryExtraerMagnitudNormalizada(
        string? texto,
        out decimal magnitudBase,
        out string familiaUnidad)
    {
        magnitudBase = 0m;
        familiaUnidad = string.Empty;

        if (string.IsNullOrWhiteSpace(texto))
        {
            return false;
        }

        var match = MedidaUnitariaRegex.Match(texto);
        if (!match.Success)
        {
            return false;
        }

        var numeroTexto = match.Groups[1].Value.Replace(',', '.');
        if (!decimal.TryParse(numeroTexto, NumberStyles.Any, CultureInfo.InvariantCulture, out var valor) || valor <= 0)
        {
            return false;
        }

        var unidad = match.Groups[2].Value.ToLowerInvariant();
        switch (unidad)
        {
            case "kg":
            case "kilo":
            case "kilos":
                magnitudBase = valor * 1000m;
                familiaUnidad = "MASA";
                return true;
            case "g":
            case "gr":
                magnitudBase = valor;
                familiaUnidad = "MASA";
                return true;
            case "lt":
            case "lts":
            case "litro":
            case "litros":
                magnitudBase = valor * 1000m;
                familiaUnidad = "VOLUMEN";
                return true;
            case "ml":
                magnitudBase = valor;
                familiaUnidad = "VOLUMEN";
                return true;
            case "m":
            case "mt":
            case "mts":
            case "metro":
            case "metros":
                magnitudBase = valor * 100m;
                familiaUnidad = "LONGITUD";
                return true;
            case "cm":
                magnitudBase = valor;
                familiaUnidad = "LONGITUD";
                return true;
            case "m2":
                magnitudBase = valor;
                familiaUnidad = "AREA";
                return true;
            case "m3":
                magnitudBase = valor;
                familiaUnidad = "M3";
                return true;
            default:
                return false;
        }
    }
}
