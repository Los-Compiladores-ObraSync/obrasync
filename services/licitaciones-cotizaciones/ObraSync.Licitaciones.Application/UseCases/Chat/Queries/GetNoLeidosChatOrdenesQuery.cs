using MediatR;
using ObraSync.Licitaciones.Application.DTOs;

namespace ObraSync.Licitaciones.Application.UseCases.Chat.Queries;

public record GetNoLeidosChatOrdenesQuery(
    Guid UsuarioId,
    string? RolUsuario) : IRequest<ResumenNoLeidosChatDto>;
