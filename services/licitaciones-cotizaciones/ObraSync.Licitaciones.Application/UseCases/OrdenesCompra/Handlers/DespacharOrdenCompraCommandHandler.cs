using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ObraSync.Contracts.Events;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.Mappings;
using ObraSync.Licitaciones.Application.UseCases.OrdenesCompra.Commands;

namespace ObraSync.Licitaciones.Application.UseCases.OrdenesCompra.Handlers;

public class DespacharOrdenCompraCommandHandler : IRequestHandler<DespacharOrdenCompraCommand, OrdenCompraResponseDto>
{
    private readonly IOrdenCompraRepository _ordenCompraRepository;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILicitacionRepository? _licitacionRepository;
    private readonly ILogger<DespacharOrdenCompraCommandHandler> _logger;

    public DespacharOrdenCompraCommandHandler(
        IOrdenCompraRepository ordenCompraRepository,
        IPublishEndpoint publishEndpoint,
        IUnitOfWork unitOfWork,
        ILicitacionRepository? licitacionRepository = null,
        ILogger<DespacharOrdenCompraCommandHandler>? logger = null)
    {
        _ordenCompraRepository = ordenCompraRepository ?? throw new ArgumentNullException(nameof(ordenCompraRepository));
        _publishEndpoint = publishEndpoint ?? throw new ArgumentNullException(nameof(publishEndpoint));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _licitacionRepository = licitacionRepository;
        _logger = logger ?? NullLogger<DespacharOrdenCompraCommandHandler>.Instance;
    }

    public async Task<OrdenCompraResponseDto> Handle(
        DespacharOrdenCompraCommand request,
        CancellationToken cancellationToken = default)
    {
        if (request.OrdenCompraId == Guid.Empty)
        {
            throw new ArgumentException("El identificador de la orden de compra es obligatorio.", nameof(request));
        }

        if (request.UsuarioId == Guid.Empty)
        {
            throw new ArgumentException("El identificador del proveedor autenticado es obligatorio.", nameof(request));
        }

        var correlationId = request.CorrelationId != Guid.Empty
            ? request.CorrelationId
            : Guid.NewGuid();

        using var logScope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        });

        var ordenCompra = await _ordenCompraRepository.GetByIdAsync(request.OrdenCompraId, cancellationToken)
            ?? throw new KeyNotFoundException($"No se encontró la orden de compra con ID {request.OrdenCompraId}.");

        if (ordenCompra.ProveedorId != request.UsuarioId)
        {
            throw new UnauthorizedAccessException(
                "Solo el proveedor titular de la orden de compra puede marcarla como despachada.");
        }

        if (ordenCompra.ObraId == Guid.Empty && _licitacionRepository is not null)
        {
            var licitacion = await _licitacionRepository.GetByIdAsync(ordenCompra.LicitacionId, cancellationToken);
            if (licitacion is not null)
            {
                ordenCompra.ObraId = licitacion.ObraId;
            }
        }

        var ahoraUtc = DateTime.UtcNow;
        var despachoAplicado = ordenCompra.Despachar(
            despachadaPor: request.UsuarioId,
            fechaDespachoUtc: ahoraUtc,
            fechaEstimadaLlegada: request.FechaEstimadaLlegada,
            observacion: request.Observacion);

        if (!despachoAplicado)
        {
            _logger.LogInformation(
                "OrdenCompra {OrdenCompraId} ya se encontraba despachada; respuesta idempotente sin republicar evento. CorrelationId {CorrelationId}",
                ordenCompra.OrdenCompraId,
                correlationId);

            return ordenCompra.ToDto();
        }

        await _ordenCompraRepository.UpdateAsync(ordenCompra, cancellationToken);

        var evento = new OrdenCompraDespachadaEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: ahoraUtc,
            CorrelationId: correlationId,
            OrdenCompraId: ordenCompra.OrdenCompraId,
            ObraId: ordenCompra.ObraId,
            FechaDespacho: ordenCompra.FechaDespacho!.Value,
            FechaEstimadaLlegada: ordenCompra.FechaEstimadaLlegada,
            ObservacionDespacho: ordenCompra.Observacion);

        await _publishEndpoint.Publish<OrdenCompraDespachadaEvent>(evento, cancellationToken);

        // El evento se publica antes de guardar a propósito (#135 / #203): con el outbox transaccional,
        // SaveChangesAsync persiste en la misma transacción el cambio de estado y el mensaje saliente.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "OrdenCompra {OrdenCompraId} marcada como despachada por Proveedor {ProveedorId}. CorrelationId {CorrelationId}",
            ordenCompra.OrdenCompraId,
            request.UsuarioId,
            correlationId);

        return ordenCompra.ToDto();
    }
}
