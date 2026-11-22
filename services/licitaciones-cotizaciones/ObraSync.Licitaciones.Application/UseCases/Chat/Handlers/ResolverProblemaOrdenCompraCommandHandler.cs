using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.Mappings;
using ObraSync.Licitaciones.Application.UseCases.Chat.Commands;

namespace ObraSync.Licitaciones.Application.UseCases.Chat.Handlers;

public class ResolverProblemaOrdenCompraCommandHandler
    : IRequestHandler<ResolverProblemaOrdenCompraCommand, ProblemaOrdenEstadoDto>
{
    private readonly IOrdenCompraRepository _ordenCompraRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IChatOrdenNotifier? _chatOrdenNotifier;
    private readonly ILogger<ResolverProblemaOrdenCompraCommandHandler> _logger;

    public ResolverProblemaOrdenCompraCommandHandler(
        IOrdenCompraRepository ordenCompraRepository,
        IUnitOfWork unitOfWork,
        IChatOrdenNotifier? chatOrdenNotifier = null,
        ILogger<ResolverProblemaOrdenCompraCommandHandler>? logger = null)
    {
        _ordenCompraRepository = ordenCompraRepository ?? throw new ArgumentNullException(nameof(ordenCompraRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _chatOrdenNotifier = chatOrdenNotifier;
        _logger = logger ?? NullLogger<ResolverProblemaOrdenCompraCommandHandler>.Instance;
    }

    public async Task<ProblemaOrdenEstadoDto> Handle(
        ResolverProblemaOrdenCompraCommand request,
        CancellationToken cancellationToken = default)
    {
        if (request.OrdenCompraId == Guid.Empty)
        {
            throw new ArgumentException("El identificador de la orden de compra es obligatorio.", nameof(request));
        }

        if (request.UsuarioId == Guid.Empty)
        {
            throw new ArgumentException("El identificador del usuario autenticado es obligatorio.", nameof(request));
        }

        var rolNormalizado = ChatOrdenAutorizacionHelper.NormalizarRolChat(request.RolUsuario);
        if (rolNormalizado is not ("Comprador" or "Admin"))
        {
            throw new UnauthorizedAccessException(
                "Solo Comprador o Admin pueden marcar como resuelto el problema de una orden de compra.");
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

        var ahoraUtc = DateTime.UtcNow;
        var cambioAplicado = ordenCompra.ResolverProblema(ahoraUtc, request.UsuarioId);

        var dto = ordenCompra.ToProblemaEstadoDto();
        if (!cambioAplicado)
        {
            _logger.LogInformation(
                "OrdenCompra {OrdenCompraId} no tenía un problema abierto; respuesta idempotente. CorrelationId {CorrelationId}",
                ordenCompra.OrdenCompraId,
                correlationId);
            return dto;
        }

        await _ordenCompraRepository.UpdateAsync(ordenCompra, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (_chatOrdenNotifier is not null)
        {
            try
            {
                await _chatOrdenNotifier.NotificarEstadoProblemaAsync(dto, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "No se pudo notificar por SignalR la resolución del problema en OrdenCompra {OrdenCompraId}. CorrelationId {CorrelationId}",
                    dto.OrdenCompraId,
                    correlationId);
            }
        }

        _logger.LogInformation(
            "Problema de OrdenCompra {OrdenCompraId} resuelto manualmente por {Rol} {UsuarioId}. CorrelationId {CorrelationId}",
            ordenCompra.OrdenCompraId,
            rolNormalizado,
            request.UsuarioId,
            correlationId);

        return dto;
    }
}
