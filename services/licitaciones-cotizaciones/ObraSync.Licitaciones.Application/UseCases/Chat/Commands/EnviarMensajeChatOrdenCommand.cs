using MediatR;
using ObraSync.Licitaciones.Application.DTOs;

namespace ObraSync.Licitaciones.Application.UseCases.Chat.Commands;

public record EnviarMensajeChatOrdenCommand(
    Guid OrdenCompraId,
    Guid UsuarioId,
    string? UsuarioNombre,
    string? RolUsuario,
    string? Texto,
    Guid CorrelationId = default) : IRequest<MensajeChatOrdenDto>;
