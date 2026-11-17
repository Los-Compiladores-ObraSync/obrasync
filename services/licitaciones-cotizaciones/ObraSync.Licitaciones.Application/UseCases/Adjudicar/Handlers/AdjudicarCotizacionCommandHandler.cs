using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Domain.Entities;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ObraSync.Contracts.Events;
using ObraSync.Licitaciones.Application.UseCases.Adjudicar.Commands;

namespace ObraSync.Licitaciones.Application.UseCases.Adjudicar.Handlers;

public class AdjudicarCotizacionCommandHandler : IRequestHandler<AdjudicarCotizacionCommand, OrdenCompraResponseDto>
{
    private readonly ILicitacionRepository _licitacionRepository;
    private readonly ICotizacionRepository _cotizacionRepository;
    private readonly IOrdenCompraRepository _ordenCompraRepository;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<AdjudicarCotizacionCommandHandler> _logger;

    public AdjudicarCotizacionCommandHandler(
        ILicitacionRepository licitacionRepository,
        ICotizacionRepository cotizacionRepository,
        IOrdenCompraRepository ordenCompraRepository,
        IPublishEndpoint publishEndpoint,
        IUnitOfWork unitOfWork,
        ILogger<AdjudicarCotizacionCommandHandler>? logger = null)
    {
        _licitacionRepository = licitacionRepository;
        _cotizacionRepository = cotizacionRepository;
        _ordenCompraRepository = ordenCompraRepository;
        _publishEndpoint = publishEndpoint;
        _unitOfWork = unitOfWork;
        _logger = logger ?? NullLogger<AdjudicarCotizacionCommandHandler>.Instance;
    }

    public async Task<OrdenCompraResponseDto> Handle(AdjudicarCotizacionCommand request, CancellationToken cancellationToken)
    {
        var correlationId = request.CorrelationId != Guid.Empty
            ? request.CorrelationId
            : Guid.NewGuid();

        using var logScope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        });

        // 1. Obtener y validar Licitación
        var licitacion = await _licitacionRepository.GetByIdAsync(request.LicitacionId, cancellationToken)
            ?? throw new KeyNotFoundException($"No se encontró la licitación con ID {request.LicitacionId}.");

        if (licitacion.Estado != EstadoLicitacion.Abierta)
        {
            throw new InvalidOperationException("La licitación ya se encuentra adjudicada o cerrada.");
        }

        // 2. Obtener y validar Cotización
        var cotizacion = await _cotizacionRepository.GetByIdAsync(request.CotizacionId, cancellationToken)
            ?? throw new KeyNotFoundException($"No se encontró la cotización con ID {request.CotizacionId}.");

        if (cotizacion.LicitacionId != request.LicitacionId)
        {
            throw new ArgumentException("La cotización no pertenece a la licitación especificada.");
        }

        // 3. Cruzar cada ítem cotizado con el ítem pedido en la licitación (para la cantidad).
        var itemsPorArticulo = new Dictionary<Guid, LicitacionItem>();
        foreach (var li in licitacion.Items)
        {
            itemsPorArticulo.TryAdd(li.ArticuloId, li);
        }

        foreach (var ci in cotizacion.Items)
        {
            if (!itemsPorArticulo.ContainsKey(ci.ArticuloId))
            {
                throw new ArgumentException(
                    $"La cotización incluye el artículo {ci.ArticuloId}, que no pertenece a la licitación.");
            }
        }

        var requisicion = await _licitacionRepository.ObtenerRequisicionAsync(licitacion.RequisicionId, cancellationToken);

        decimal subtotalItems = cotizacion.Items.Sum(ci => ci.CalcularSubtotal(itemsPorArticulo[ci.ArticuloId].Cantidad));
        decimal montoTotal = subtotalItems + cotizacion.Flete;

        // 4. Crear la entidad Orden de Compra
        var ordenCompraId = Guid.NewGuid();
        var ordenCompra = new OrdenCompra
        {
            OrdenCompraId = ordenCompraId,
            LicitacionId = licitacion.LicitacionId,
            CotizacionId = cotizacion.CotizacionId,
            ProveedorId = cotizacion.ProveedorId,
            Estado = "Emitida",
            FechaEmision = DateTime.UtcNow,
            TotalFlete = cotizacion.Flete,
            Items = cotizacion.Items.Select(ci => {
                var licItem = itemsPorArticulo[ci.ArticuloId];
                var cantidadEfectiva = ci.CantidadOfrecida is > 0 ? ci.CantidadOfrecida.Value : licItem.Cantidad;
                var descripcionEfectiva = !string.IsNullOrWhiteSpace(ci.PresentacionOfrecida)
                    ? $"{licItem.Descripcion} ({ci.PresentacionOfrecida})"
                    : licItem.Descripcion;
                return new OrdenCompraItem
                {
                    Id = Guid.NewGuid(),
                    OrdenCompraId = ordenCompraId,
                    ArticuloId = ci.ArticuloId,
                    Descripcion = descripcionEfectiva,
                    Cantidad = cantidadEfectiva,
                    UnidadMedida = licItem.UnidadMedida,
                    PrecioUnitario = ci.PrecioUnitario
                };
            }).ToList()
        };

        // Actualizar estado de la licitación
        licitacion.Estado = EstadoLicitacion.Adjudicada;

        await _ordenCompraRepository.AddAsync(ordenCompra, cancellationToken);
        await _licitacionRepository.UpdateAsync(licitacion, cancellationToken);

        // 5. Publicar el evento de integración con MassTransit propagando el CorrelationId del pedido original (#104)
        var evento = new OrdenCompraEmitidaEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow,
            CorrelationId: correlationId,
            OrdenCompraId: ordenCompra.OrdenCompraId,
            LicitacionId: licitacion.LicitacionId,
            RequisicionId: licitacion.RequisicionId,
            ObraId: licitacion.ObraId,
            ProveedorId: cotizacion.ProveedorId,
            Etapa: requisicion?.Etapa,
            MontoTotal: montoTotal,
            Items: ordenCompra.Items.Select(i => new OrdenCompraItemDto(
                i.ArticuloId,
                i.Descripcion,
                i.Cantidad,
                i.UnidadMedida,
                i.PrecioUnitario
            )).ToList()
        );

        await _publishEndpoint.Publish<OrdenCompraEmitidaEvent>(evento, cancellationToken);

        // El evento se publica ANTES de guardar a propósito (#135): con el outbox, este único
        // SaveChanges guarda en la misma transacción la adjudicación, la OC y el evento.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Licitación {LicitacionId} adjudicada y OrdenCompra {OrdenCompraId} emitida. CorrelationId {CorrelationId}",
            licitacion.LicitacionId,
            ordenCompra.OrdenCompraId,
            correlationId);

        return new OrdenCompraResponseDto(
            ordenCompra.OrdenCompraId,
            licitacion.LicitacionId,
            cotizacion.CotizacionId,
            ordenCompra.Estado,
            ordenCompra.FechaEmision
        );
    }
}