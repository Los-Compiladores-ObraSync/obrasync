using Microsoft.Extensions.DependencyInjection;
using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Commands;

namespace ObraSync.Recepcion.Application.UseCases.Recepciones.Handlers;

/// <summary>
/// Caso de uso para liquidar administrativamente una orden de compra (#92).
/// Bloquea la liquidación mientras exista saldo pendiente de recepción (criterio CU-06).
/// </summary>
public class LiquidarOrdenCompraCommandHandler : ICommandHandler<LiquidarOrdenCompraCommand, OrdenCompraLiquidadaDto>
{
    private readonly IOrdenCompraRepository _ordenCompraRepository;
    private readonly IRecepcionRepository _recepcionRepository;
    private readonly IUnitOfWork _unitOfWork;

    [ActivatorUtilitiesConstructor]
    public LiquidarOrdenCompraCommandHandler(
        IOrdenCompraRepository ordenCompraRepository,
        IRecepcionRepository recepcionRepository,
        IUnitOfWork unitOfWork)
    {
        _ordenCompraRepository = ordenCompraRepository ?? throw new ArgumentNullException(nameof(ordenCompraRepository));
        _recepcionRepository = recepcionRepository ?? throw new ArgumentNullException(nameof(recepcionRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    public LiquidarOrdenCompraCommandHandler(IRecepcionPersistenceContext persistenceContext)
        : this(
            persistenceContext ?? throw new ArgumentNullException(nameof(persistenceContext)),
            persistenceContext,
            persistenceContext)
    {
    }

    public async Task<OrdenCompraLiquidadaDto> Handle(
        LiquidarOrdenCompraCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.OrdenCompraId == Guid.Empty)
        {
            throw new ArgumentException("El identificador de la orden de compra es obligatorio.", nameof(command.OrdenCompraId));
        }

        if (command.UsuarioId == Guid.Empty)
        {
            throw new ArgumentException("El identificador del usuario es obligatorio.", nameof(command.UsuarioId));
        }

        var ordenCompra = await _ordenCompraRepository.ObtenerConItemsParaActualizarAsync(
            command.OrdenCompraId,
            cancellationToken);

        if (ordenCompra is null)
        {
            throw new KeyNotFoundException($"No se encontró la orden de compra {command.OrdenCompraId}.");
        }

        var recibidosPorArticulo = await _recepcionRepository.ObtenerCantidadesRecibidasPorArticuloAsync(
            command.OrdenCompraId,
            cancellationToken);

        ordenCompra.Liquidar(command.UsuarioId, recibidosPorArticulo);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new OrdenCompraLiquidadaDto
        {
            OrdenCompraId = ordenCompra.Id,
            ObraId = ordenCompra.ObraId,
            MontoLiquidado = ordenCompra.MontoTotal,
            LiquidadaPorUsuarioId = ordenCompra.LiquidadaPorUsuarioId!.Value,
            LiquidadaEn = ordenCompra.LiquidadaEn!.Value
        };
    }
}
