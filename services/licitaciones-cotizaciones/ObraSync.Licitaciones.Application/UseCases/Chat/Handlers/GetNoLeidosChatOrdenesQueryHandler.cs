using MediatR;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.UseCases.Chat.Queries;

namespace ObraSync.Licitaciones.Application.UseCases.Chat.Handlers;

public class GetNoLeidosChatOrdenesQueryHandler
    : IRequestHandler<GetNoLeidosChatOrdenesQuery, ResumenNoLeidosChatDto>
{
    private readonly IChatOrdenRepository _chatOrdenRepository;

    public GetNoLeidosChatOrdenesQueryHandler(IChatOrdenRepository chatOrdenRepository)
    {
        _chatOrdenRepository = chatOrdenRepository ?? throw new ArgumentNullException(nameof(chatOrdenRepository));
    }

    public async Task<ResumenNoLeidosChatDto> Handle(
        GetNoLeidosChatOrdenesQuery request,
        CancellationToken cancellationToken = default)
    {
        if (request.UsuarioId == Guid.Empty)
        {
            throw new ArgumentException("El identificador del usuario autenticado es obligatorio.", nameof(request));
        }

        var rolNormalizado = ChatOrdenAutorizacionHelper.NormalizarRolChat(request.RolUsuario)
            ?? throw new UnauthorizedAccessException(
                "Solo Comprador, Admin o Proveedor pueden consultar mensajes no leídos.");

        var soloProveedor = rolNormalizado == "Proveedor";
        var ordenes = await _chatOrdenRepository.GetNoLeidosPorUsuarioAsync(
            request.UsuarioId,
            soloProveedor,
            cancellationToken);

        var total = ordenes.Sum(x => x.MensajesNoLeidos);
        return new ResumenNoLeidosChatDto(total, ordenes);
    }
}
