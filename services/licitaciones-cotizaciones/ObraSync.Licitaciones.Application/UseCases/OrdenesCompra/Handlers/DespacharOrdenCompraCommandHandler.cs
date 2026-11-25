using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ObraSync.Contracts.Events;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.Mappings;
using ObraSync.Licitaciones.Application.UseCases.OrdenesCompra.Commands;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.UseCases.OrdenesCompra.Handlers;

public class DespacharOrdenCompraCommandHandler : IRequestHandler<DespacharOrdenCompraCommand, OrdenCompraResponseDto>
{
    private readonly IOrdenCompraRepository _ordenCompraRepository;
    private readonly ILicitacionEventPublisher _eventPublisher;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEstadoPresupuestarioOrden _estadoPresupuestarioOrden;
    private readonly ILicitacionRepository? _licitacionRepository;
    private readonly ILogger<DespacharOrdenCompraCommandHandler> _logger;
    private readonly IChatOrdenRepository? _chatOrdenRepository;
    private readonly IChatOrdenNotifier? _chatOrdenNotifier;

    public DespacharOrdenCompraCommandHandler(
        IOrdenCompraRepository ordenCompraRepository,
        ILicitacionEventPublisher eventPublisher,
        IUnitOfWork unitOfWork,
        IEstadoPresupuestarioOrden estadoPresupuestarioOrden,
        ILicitacionRepository? licitacionRepository = null,
        ILogger<DespacharOrdenCompraCommandHandler>? logger = null,
        IChatOrdenRepository? chatOrdenRepository = null,
        IChatOrdenNotifier? chatOrdenNotifier = null)
    {
        _ordenCompraRepository = ordenCompraRepository ?? throw new ArgumentNullException(nameof(ordenCompraRepository));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _estadoPresupuestarioOrden = estadoPresupuestarioOrden
            ?? throw new ArgumentNullException(nameof(estadoPresupuestarioOrden));
        _licitacionRepository = licitacionRepository;
        _logger = logger ?? NullLogger<DespacharOrdenCompraCommandHandler>.Instance;
        _chatOrdenRepository = chatOrdenRepository;
        _chatOrdenNotifier = chatOrdenNotifier;
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

        var bloqueada = await _estadoPresupuestarioOrden.EstaBloqueadaAsync(
            ordenCompra.OrdenCompraId,
            cancellationToken);

        if (bloqueada == true)
        {
            throw new InvalidOperationException(
                "La orden está bloqueada por presupuesto y requiere autorización del administrador.");
        }

        if (bloqueada is null)
        {
            throw new InvalidOperationException(
                "Todavía no se pudo verificar el estado presupuestario de la orden. Reintentá en unos segundos.");
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
        var esRedespacho = ordenCompra.PuedeRedespachar;

        ordenCompra.Despachar(
            despachadaPor: request.UsuarioId,
            fechaDespachoUtc: ahoraUtc,
            fechaEstimadaLlegada: request.FechaEstimadaLlegada,
            observacion: request.Observacion);

        await _ordenCompraRepository.UpdateAsync(ordenCompra, cancellationToken);

        var evento = new OrdenCompraDespachadaEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: ahoraUtc,
            CorrelationId: correlationId,
            OrdenCompraId: ordenCompra.OrdenCompraId,
            ObraId: ordenCompra.ObraId,
            FechaDespacho: ordenCompra.FechaDespacho!.Value,
            FechaEstimadaLlegada: ordenCompra.FechaEstimadaLlegada,
            ObservacionDespacho: ordenCompra.Observacion,
            EsRedespacho: esRedespacho,
            NumeroDespacho: ordenCompra.NumeroDespacho);

        await _eventPublisher.PublishAsync(evento, cancellationToken);

        var textoAviso = esRedespacho
            ? (string.IsNullOrWhiteSpace(ordenCompra.Observacion)
                ? $"El Proveedor despachó el remanente de la orden (envío #{ordenCompra.NumeroDespacho})."
                : $"El Proveedor despachó el remanente de la orden (envío #{ordenCompra.NumeroDespacho}): {ordenCompra.Observacion}")
            : "El Proveedor despachó la orden";

        MensajeChatOrden? mensajeSistema = null;
        if (_chatOrdenRepository is not null)
        {
            mensajeSistema = MensajeChatOrden.CrearDelSistema(
                ordenCompra.OrdenCompraId,
                textoAviso,
                ahoraUtc);
            await _chatOrdenRepository.AddMensajeAsync(mensajeSistema, cancellationToken);
        }

        if (esRedespacho)
        {
            var fragmento = mensajeSistema is not null
                ? mensajeSistema.ObtenerFragmento()
                : (textoAviso.Length <= MensajeChatOrden.MaxLongitudFragmento
                    ? textoAviso
                    : string.Concat(textoAviso.AsSpan(0, MensajeChatOrden.MaxLongitudFragmento - 1), "…"));

            var eventoCampanita = new MensajeChatEnviadoEvent(
                EventId: Guid.NewGuid(),
                OccurredAt: ahoraUtc,
                CorrelationId: correlationId,
                OrdenCompraId: ordenCompra.OrdenCompraId,
                Remitente: new RemitenteChatEvent(
                    UsuarioId: request.UsuarioId,
                    Nombre: "Proveedor",
                    Rol: "Proveedor"),
                Destinatarios: new DestinatariosChatEvent(
                    UsuarioIds: Array.Empty<Guid>(),
                    Roles: new[] { "Comprador", "Admin", "Capataz" }),
                Fragmento: fragmento);

            await _eventPublisher.PublishAsync(eventoCampanita, cancellationToken);
        }

        // El evento se publica antes de guardar a propósito (#135 / #203): con el outbox transaccional,
        // SaveChangesAsync persiste en la misma transacción el cambio de estado, el mensaje del chat y el evento saliente.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (mensajeSistema is not null && _chatOrdenNotifier is not null)
        {
            try
            {
                await _chatOrdenNotifier.NotificarMensajeAsync(mensajeSistema.ToDto(), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "No se pudo notificar por SignalR el mensaje de despacho en OrdenCompra {OrdenCompraId}. CorrelationId {CorrelationId}",
                    ordenCompra.OrdenCompraId,
                    correlationId);
            }
        }

        _logger.LogInformation(
            "OrdenCompra {OrdenCompraId} {AccionDespacho} (#{NumeroDespacho}) por Proveedor {ProveedorId}. CorrelationId {CorrelationId}",
            ordenCompra.OrdenCompraId,
            esRedespacho ? "re-despachada" : "marcada como despachada",
            ordenCompra.NumeroDespacho,
            request.UsuarioId,
            correlationId);

        return ordenCompra.ToDto();
    }
}
