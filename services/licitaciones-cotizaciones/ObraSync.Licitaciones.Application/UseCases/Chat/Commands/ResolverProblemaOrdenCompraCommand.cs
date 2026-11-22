using MediatR;
using ObraSync.Licitaciones.Application.DTOs;

namespace ObraSync.Licitaciones.Application.UseCases.Chat.Commands;

public record ResolverProblemaOrdenCompraCommand(
    Guid OrdenCompraId,
    Guid UsuarioId,
    string? RolUsuario,
    Guid CorrelationId = default) : IRequest<ProblemaOrdenEstadoDto>;
