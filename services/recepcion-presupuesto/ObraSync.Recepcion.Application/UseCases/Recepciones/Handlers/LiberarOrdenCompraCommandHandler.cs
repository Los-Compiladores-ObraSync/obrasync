using Microsoft.Extensions.DependencyInjection;
using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Exceptions;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Commands;
using ObraSync.Recepcion.Domain.Presupuestos;

namespace ObraSync.Recepcion.Application.UseCases.Recepciones.Handlers;

public class LiberarOrdenCompraCommandHandler : ICommandHandler<LiberarOrdenCompraCommand, OrdenCompraLiberadaDto>
{
    private readonly IOrdenCompraRepository _ordenCompraRepository;
    private readonly IPartidaPresupuestariaRepository _partidaRepository;
    private readonly IUnitOfWork _unitOfWork;

    [ActivatorUtilitiesConstructor]
    public LiberarOrdenCompraCommandHandler(
        IOrdenCompraRepository ordenCompraRepository,
        IPartidaPresupuestariaRepository partidaRepository,
        IUnitOfWork unitOfWork)
    {
        _ordenCompraRepository = ordenCompraRepository ?? throw new ArgumentNullException(nameof(ordenCompraRepository));
        _partidaRepository = partidaRepository ?? throw new ArgumentNullException(nameof(partidaRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    public LiberarOrdenCompraCommandHandler(IRecepcionPersistenceContext persistenceContext)
        : this(
            persistenceContext ?? throw new ArgumentNullException(nameof(persistenceContext)),
            persistenceContext,
            persistenceContext)
    {
    }

    public async Task<OrdenCompraLiberadaDto> Handle(
        LiberarOrdenCompraCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command is null)
            throw new ArgumentNullException(nameof(command));

        if (command.OrdenCompraId == Guid.Empty)
            throw new ArgumentException("El identificador de la orden de compra es obligatorio.", nameof(command.OrdenCompraId));

        if (command.UsuarioId == Guid.Empty)
            throw new ArgumentException("El usuario administrador que libera la orden es obligatorio.", nameof(command.UsuarioId));

        var ordenCompra = await _ordenCompraRepository.ObtenerParaActualizarAsync(command.OrdenCompraId, cancellationToken);

        if (ordenCompra is null)
        {
            throw new KeyNotFoundException($"No se encontró la orden de compra con id '{command.OrdenCompraId}'.");
        }

        // Registra quién y cuándo liberó la OC, y valida que estuviera Bloqueada (#96)
        ordenCompra.Liberar(command.UsuarioId, command.Motivo);

        // Decisión #154 (#96): al liberar una OC bloqueada, se compromete su monto en la partida de su (ObraId, Etapa)
        var partidasObra = await _partidaRepository.ObtenerPartidasPorObraParaActualizarAsync(
            ordenCompra.ObraId,
            cancellationToken);

        var partida = EmparejadorPartidaPresupuestaria.BuscarPartida(partidasObra, ordenCompra.Etapa);
        if (partida is not null)
        {
            partida.ComprometerMonto(ordenCompra.MontoTotal);
            ordenCompra.AsignarPartida();
        }

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrenciaPersistenciaException)
        {
            throw new RecepcionConflictoException(
                "La partida presupuestaria fue modificada simultáneamente por otra operación. Reintentá la liberación.");
        }

        return new OrdenCompraLiberadaDto(
            OrdenCompraId: ordenCompra.Id,
            ObraId: ordenCompra.ObraId,
            Estado: ordenCompra.Estado,
            LiberadaPorUsuarioId: ordenCompra.LiberadaPorUsuarioId!.Value,
            LiberadaEn: ordenCompra.LiberadaEn!.Value,
            MotivoLiberacion: ordenCompra.MotivoLiberacion,
            MontoTotal: ordenCompra.MontoTotal,
            PartidaPresupuestariaId: partida?.Id,
            MontoComprometidoPartida: partida?.MontoComprometido);
    }
}
