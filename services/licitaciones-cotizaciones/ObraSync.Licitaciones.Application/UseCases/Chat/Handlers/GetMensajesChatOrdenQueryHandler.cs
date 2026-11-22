using MediatR;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.Mappings;
using ObraSync.Licitaciones.Application.UseCases.Chat.Queries;

namespace ObraSync.Licitaciones.Application.UseCases.Chat.Handlers;

public class GetMensajesChatOrdenQueryHandler
    : IRequestHandler<GetMensajesChatOrdenQuery, PaginaDe<MensajeChatOrdenDto>>
{
    private readonly IOrdenCompraRepository _ordenCompraRepository;
    private readonly IChatOrdenRepository _chatOrdenRepository;

    public GetMensajesChatOrdenQueryHandler(
        IOrdenCompraRepository ordenCompraRepository,
        IChatOrdenRepository chatOrdenRepository)
    {
        _ordenCompraRepository = ordenCompraRepository ?? throw new ArgumentNullException(nameof(ordenCompraRepository));
        _chatOrdenRepository = chatOrdenRepository ?? throw new ArgumentNullException(nameof(chatOrdenRepository));
    }

    public async Task<PaginaDe<MensajeChatOrdenDto>> Handle(
        GetMensajesChatOrdenQuery request,
        CancellationToken cancellationToken = default)
    {
        var errorPaginacion = request.Paginacion.Validar();
        if (errorPaginacion is not null)
        {
            throw new ArgumentException(errorPaginacion, nameof(request));
        }

        await ChatOrdenAutorizacionHelper.ValidarAccesoChatAsync(
            _ordenCompraRepository,
            request.OrdenCompraId,
            request.UsuarioId,
            request.RolUsuario,
            cancellationToken);

        var pagina = await _chatOrdenRepository.GetMensajesPorOrdenAsync(
            request.OrdenCompraId,
            request.Paginacion,
            cancellationToken);

        var dtos = pagina.Items.Select(m => m.ToDto()).ToList();
        return new PaginaDe<MensajeChatOrdenDto>(dtos, pagina.Total);
    }
}
