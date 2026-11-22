using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.Mappings;
using ObraSync.Licitaciones.Application.UseCases.Chat.Commands;

namespace ObraSync.Licitaciones.Application.UseCases.Chat.Handlers;

public class MarcarChatOrdenLeidoCommandHandler
    : IRequestHandler<MarcarChatOrdenLeidoCommand, LecturaChatResponseDto>
{
    private readonly IOrdenCompraRepository _ordenCompraRepository;
    private readonly IChatOrdenRepository _chatOrdenRepository;
    private readonly IChatOrdenNotifier? _chatOrdenNotifier;
    private readonly ILogger<MarcarChatOrdenLeidoCommandHandler> _logger;

    public MarcarChatOrdenLeidoCommandHandler(
        IOrdenCompraRepository ordenCompraRepository,
        IChatOrdenRepository chatOrdenRepository,
        IChatOrdenNotifier? chatOrdenNotifier = null,
        ILogger<MarcarChatOrdenLeidoCommandHandler>? logger = null)
    {
        _ordenCompraRepository = ordenCompraRepository ?? throw new ArgumentNullException(nameof(ordenCompraRepository));
        _chatOrdenRepository = chatOrdenRepository ?? throw new ArgumentNullException(nameof(chatOrdenRepository));
        _chatOrdenNotifier = chatOrdenNotifier;
        _logger = logger ?? NullLogger<MarcarChatOrdenLeidoCommandHandler>.Instance;
    }

    public async Task<LecturaChatResponseDto> Handle(
        MarcarChatOrdenLeidoCommand request,
        CancellationToken cancellationToken = default)
    {
        await ChatOrdenAutorizacionHelper.ValidarAccesoChatAsync(
            _ordenCompraRepository,
            request.OrdenCompraId,
            request.UsuarioId,
            request.RolUsuario,
            cancellationToken);

        var ahoraUtc = DateTime.UtcNow;
        var lectura = await _chatOrdenRepository.UpsertLecturaAsync(
            request.OrdenCompraId,
            request.UsuarioId,
            ahoraUtc,
            cancellationToken);

        var dto = lectura.ToDto();
        if (_chatOrdenNotifier is not null)
        {
            try
            {
                await _chatOrdenNotifier.NotificarLecturaAsync(dto, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "No se pudo notificar por SignalR la lectura de OrdenCompra {OrdenCompraId} para usuario {UsuarioId}.",
                    request.OrdenCompraId,
                    request.UsuarioId);
            }
        }

        return dto;
    }
}
