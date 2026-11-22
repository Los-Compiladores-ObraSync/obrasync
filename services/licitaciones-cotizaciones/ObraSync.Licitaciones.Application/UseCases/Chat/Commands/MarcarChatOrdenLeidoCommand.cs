using MediatR;
using ObraSync.Licitaciones.Application.DTOs;

namespace ObraSync.Licitaciones.Application.UseCases.Chat.Commands;

public record MarcarChatOrdenLeidoCommand(
    Guid OrdenCompraId,
    Guid UsuarioId,
    string? RolUsuario) : IRequest<LecturaChatResponseDto>;
