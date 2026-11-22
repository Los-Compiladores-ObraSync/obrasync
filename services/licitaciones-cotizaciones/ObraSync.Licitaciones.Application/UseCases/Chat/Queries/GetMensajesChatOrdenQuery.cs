using MediatR;
using ObraSync.Licitaciones.Application.DTOs;

namespace ObraSync.Licitaciones.Application.UseCases.Chat.Queries;

public record GetMensajesChatOrdenQuery(
    Guid OrdenCompraId,
    Guid UsuarioId,
    string? RolUsuario,
    ParametrosPagina Paginacion) : IRequest<PaginaDe<MensajeChatOrdenDto>>;
