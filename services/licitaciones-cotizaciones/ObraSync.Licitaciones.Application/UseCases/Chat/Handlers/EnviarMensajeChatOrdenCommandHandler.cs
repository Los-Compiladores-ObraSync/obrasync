using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ObraSync.Contracts.Events;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.Mappings;
using ObraSync.Licitaciones.Application.UseCases.Chat.Commands;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.UseCases.Chat.Handlers;

public class EnviarMensajeChatOrdenCommandHandler
    : IRequestHandler<EnviarMensajeChatOrdenCommand, MensajeChatOrdenDto>
{
    private static readonly string[] RolesCompras = ["Comprador", "Admin"];

    private readonly IOrdenCompraRepository _ordenCompraRepository;
    private readonly IChatOrdenRepository _chatOrdenRepository;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IChatOrdenNotifier? _chatOrdenNotifier;
    private readonly ILogger<EnviarMensajeChatOrdenCommandHandler> _logger;

    public EnviarMensajeChatOrdenCommandHandler(
        IOrdenCompraRepository ordenCompraRepository,
        IChatOrdenRepository chatOrdenRepository,
        IPublishEndpoint publishEndpoint,
        IUnitOfWork unitOfWork,
        IChatOrdenNotifier? chatOrdenNotifier = null,
        ILogger<EnviarMensajeChatOrdenCommandHandler>? logger = null)
    {
        _ordenCompraRepository = ordenCompraRepository ?? throw new ArgumentNullException(nameof(ordenCompraRepository));
        _chatOrdenRepository = chatOrdenRepository ?? throw new ArgumentNullException(nameof(chatOrdenRepository));
        _publishEndpoint = publishEndpoint ?? throw new ArgumentNullException(nameof(publishEndpoint));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _chatOrdenNotifier = chatOrdenNotifier;
        _logger = logger ?? NullLogger<EnviarMensajeChatOrdenCommandHandler>.Instance;
    }

    public async Task<MensajeChatOrdenDto> Handle(
        EnviarMensajeChatOrdenCommand request,
        CancellationToken cancellationToken = default)
    {
        var correlationId = request.CorrelationId != Guid.Empty
            ? request.CorrelationId
            : Guid.NewGuid();

        using var logScope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        });

        var (ordenCompra, rolNormalizado) = await ChatOrdenAutorizacionHelper.ValidarAccesoChatAsync(
            _ordenCompraRepository,
            request.OrdenCompraId,
            request.UsuarioId,
            request.RolUsuario,
            cancellationToken);

        var ahoraUtc = DateTime.UtcNow;
        var mensaje = MensajeChatOrden.CrearDePersona(
            ordenCompraId: ordenCompra.OrdenCompraId,
            autorId: request.UsuarioId,
            autorNombre: request.UsuarioNombre,
            autorRol: rolNormalizado,
            texto: request.Texto,
            fechaCreacionUtc: ahoraUtc);

        await _chatOrdenRepository.AddMensajeAsync(mensaje, cancellationToken);

        var destinatarios = rolNormalizado == "Proveedor"
            ? new DestinatariosChatEvent(Array.Empty<Guid>(), RolesCompras)
            : new DestinatariosChatEvent(new[] { ordenCompra.ProveedorId }, Array.Empty<string>());

        var evento = new MensajeChatEnviadoEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: ahoraUtc,
            CorrelationId: correlationId,
            OrdenCompraId: ordenCompra.OrdenCompraId,
            Remitente: new RemitenteChatEvent(
                UsuarioId: request.UsuarioId,
                Nombre: mensaje.AutorNombre,
                Rol: rolNormalizado),
            Destinatarios: destinatarios,
            Fragmento: mensaje.ObtenerFragmento());

        await _publishEndpoint.Publish<MensajeChatEnviadoEvent>(evento, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var dto = mensaje.ToDto();
        if (_chatOrdenNotifier is not null)
        {
            try
            {
                await _chatOrdenNotifier.NotificarMensajeAsync(dto, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "No se pudo notificar por SignalR el mensaje {MensajeId} en OrdenCompra {OrdenCompraId}. CorrelationId {CorrelationId}",
                    dto.Id,
                    dto.OrdenCompraId,
                    correlationId);
            }
        }

        _logger.LogInformation(
            "Mensaje de chat {MensajeId} enviado por {Rol} {UsuarioId} en OrdenCompra {OrdenCompraId}. CorrelationId {CorrelationId}",
            dto.Id,
            rolNormalizado,
            request.UsuarioId,
            dto.OrdenCompraId,
            correlationId);

        return dto;
    }
}
