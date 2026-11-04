using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.Mappings;
using ObraSync.Licitaciones.Application.UseCases.Cotizaciones.Commands;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.UseCases.Cotizaciones.Handlers;

public class CrearCotizacionCommandHandler
{
    private readonly ILicitacionRepository _licitacionRepository;
    private readonly ICotizacionRepository _cotizacionRepository;

    public CrearCotizacionCommandHandler(
        ILicitacionRepository licitacionRepository,
        ICotizacionRepository cotizacionRepository)
    {
        _licitacionRepository = licitacionRepository ?? throw new ArgumentNullException(nameof(licitacionRepository));
        _cotizacionRepository = cotizacionRepository ?? throw new ArgumentNullException(nameof(cotizacionRepository));
    }

    public async Task<CotizacionResponseDto> Handle(
        CrearCotizacionCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.LicitacionId == Guid.Empty)
        {
            throw new ArgumentException("El identificador de la licitación es obligatorio.");
        }

        if (command.ProveedorId == Guid.Empty)
        {
            throw new ArgumentException("El identificador del proveedor es obligatorio.");
        }

        if (command.Flete < 0)
        {
            throw new ArgumentException("El flete no puede ser negativo.");
        }

        if (command.PlazoEntregaDias <= 0)
        {
            throw new ArgumentException("El plazo de entrega en días debe ser mayor a cero.");
        }

        if (command.Items == null || command.Items.Count == 0)
        {
            throw new ArgumentException("La cotización debe incluir al menos un ítem.");
        }

        if (command.Items.Any(i => i.ArticuloId == Guid.Empty || i.PrecioUnitario <= 0))
        {
            throw new ArgumentException("Todos los ítems cotizados deben tener un artículo válido y un precioUnitario mayor a cero.");
        }

        // 1. Verificar existencia de la licitación (404 Not Found)
        var licitacion = await _licitacionRepository.GetByIdAsync(command.LicitacionId, cancellationToken);
        if (licitacion == null)
        {
            throw new KeyNotFoundException($"No se encontró la licitación con Id '{command.LicitacionId}'.");
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

        // 4. Validar coincidencia exacta de artículos con el pliego de la licitación (sin faltantes, sin sobrantes ni duplicados)
        var articulosPliego = licitacion.Items
            .Select(i => i.ArticuloId)
            .Distinct()
            .ToHashSet();

        var articulosCotizados = command.Items
            .Select(i => i.ArticuloId)
            .ToList();

        if (articulosCotizados.Count != articulosPliego.Count ||
            articulosCotizados.Distinct().Count() != articulosPliego.Count ||
            !articulosCotizados.All(articulosPliego.Contains))
        {
            throw new ArgumentException("Los artículos cotizados deben coincidir exactamente con los artículos del pliego de la licitación (sin faltantes ni sobrantes).");
        }

        // 5. Validar que el proveedor no haya cotizado previamente la misma licitación (409 Conflict)
        var yaCotizo = await _cotizacionRepository.ExistsByLicitacionAndProveedorAsync(
            command.LicitacionId,
            command.ProveedorId,
            cancellationToken);

        if (yaCotizo)
        {
            throw new InvalidOperationException("El proveedor ya presentó una cotización para esta licitación.");
        }

        var cotizacionId = Guid.NewGuid();
        var cotizacion = new Cotizacion
        {
            CotizacionId = cotizacionId,
            LicitacionId = command.LicitacionId,
            ProveedorId = command.ProveedorId,
            Flete = command.Flete,
            PlazoEntregaDias = command.PlazoEntregaDias,
            FechaCreacion = DateTime.UtcNow,
            Items = command.Items
                .Select(i => new CotizacionItem
                {
                    Id = Guid.NewGuid(),
                    CotizacionId = cotizacionId,
                    ArticuloId = i.ArticuloId,
                    PrecioUnitario = i.PrecioUnitario
                })
                .ToList()
        };

        await _cotizacionRepository.AddAsync(cotizacion, cancellationToken);
        await _cotizacionRepository.SaveChangesAsync(cancellationToken);

        return cotizacion.ToDto();
    }
}
