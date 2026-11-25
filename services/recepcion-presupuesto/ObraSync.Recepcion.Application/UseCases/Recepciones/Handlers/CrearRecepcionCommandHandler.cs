using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Exceptions;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Application.Mappings;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Commands;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Validators;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Domain.Enums;
using ObraSync.Recepcion.Domain.Presupuestos;
using DetalleRecepcionEntity = ObraSync.Recepcion.Domain.Entities.DetalleRecepcion;
using RecepcionEntity = ObraSync.Recepcion.Domain.Entities.Recepcion;

namespace ObraSync.Recepcion.Application.UseCases.Recepciones.Handlers;

public class CrearRecepcionCommandHandler : ICommandHandler<CrearRecepcionCommand, RecepcionDto>
{
    private readonly IOrdenCompraRepository _ordenCompraRepository;
    private readonly IRecepcionRepository _recepcionRepository;
    private readonly IPartidaPresupuestariaRepository _partidaRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRecepcionEventPublisher _eventPublisher;
    private readonly ILogger<CrearRecepcionCommandHandler> _logger;
    private readonly ICrearRecepcionCommandValidator _validator;

    [ActivatorUtilitiesConstructor]
    public CrearRecepcionCommandHandler(
        IOrdenCompraRepository ordenCompraRepository,
        IRecepcionRepository recepcionRepository,
        IPartidaPresupuestariaRepository partidaRepository,
        IUnitOfWork unitOfWork,
        IRecepcionEventPublisher eventPublisher,
        ILogger<CrearRecepcionCommandHandler> logger,
        ICrearRecepcionCommandValidator? validator = null)
    {
        _ordenCompraRepository = ordenCompraRepository ?? throw new ArgumentNullException(nameof(ordenCompraRepository));
        _recepcionRepository = recepcionRepository ?? throw new ArgumentNullException(nameof(recepcionRepository));
        _partidaRepository = partidaRepository ?? throw new ArgumentNullException(nameof(partidaRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _validator = validator ?? new CrearRecepcionCommandValidator();
    }

    public CrearRecepcionCommandHandler(
        IRecepcionPersistenceContext persistenceContext,
        IRecepcionEventPublisher eventPublisher,
        ILogger<CrearRecepcionCommandHandler> logger,
        ICrearRecepcionCommandValidator? validator = null)
        : this(
            persistenceContext ?? throw new ArgumentNullException(nameof(persistenceContext)),
            persistenceContext,
            persistenceContext,
            persistenceContext,
            eventPublisher,
            logger,
            validator)
    {
    }

    public async Task<RecepcionDto> Handle(
        CrearRecepcionCommand command,
        CancellationToken cancellationToken = default)
    {
        var entradaValidada = _validator.ValidarEntrada(command);
        bool esEntregaConProblema = entradaValidada.EsEntregaConProblema;
        string? motivoNormalizado = entradaValidada.MotivoNormalizado;
        var fotosValidadas = entradaValidada.FotosValidadas ?? Array.Empty<FotoEvidenciaValidada>();

        var ordenCompra = await _ordenCompraRepository.ObtenerPorIdAsync(
            command.OrdenCompraId,
            cancellationToken);

        _validator.ValidarOrdenCompra(command, ordenCompra);

        // Si la última entrega se marcó con un problema, la orden volvió al proveedor: el capataz no puede
        // registrar nada más hasta que el proveedor despache de nuevo.
        var ultimaRecepcion = await _recepcionRepository.ObtenerUltimaRecepcionResumenAsync(
            command.OrdenCompraId,
            cancellationToken);

        if (ultimaRecepcion is not null
            && ultimaRecepcion.Value.Estado == EstadoRecepcion.Rechazada
            && ordenCompra.EsperaNuevoDespachoTras(ultimaRecepcion.Value.CreatedAt))
        {
            throw new RecepcionConflictoException(
                "La entrega se marcó con un problema y volvió al proveedor: hasta que la despache de nuevo no se puede registrar otra recepción.");
        }

        bool yaRecibida = await _recepcionRepository.ExisteRecepcionCompletaAsync(
            command.OrdenCompraId,
            cancellationToken);

        if (yaRecibida)
        {
            throw new RecepcionConflictoException("La orden de compra ya fue recibida por completo.");
        }

        string numeroRemito = esEntregaConProblema && string.IsNullOrWhiteSpace(command.NumeroRemito)
            ? $"PROB-{Guid.NewGuid():N}"[..21]
            : command.NumeroRemito.Trim();

        bool remitoRepetido = await _recepcionRepository.ExisteNumeroRemitoAsync(
            command.OrdenCompraId,
            numeroRemito,
            cancellationToken);

        if (remitoRepetido)
        {
            throw new RecepcionConflictoException("Ya existe una recepción con ese número de remito para esta orden de compra.");
        }

        var recibidoPrevioPorArticulo = await _recepcionRepository.ObtenerCantidadesRecibidasPorArticuloAsync(
            command.OrdenCompraId,
            cancellationToken);

        var saldosPendientes = ordenCompra.CalcularSaldosPendientes(recibidoPrevioPorArticulo);

        var recepcionId = Guid.NewGuid();
        var ahora = DateTime.UtcNow;

        var recepcion = new RecepcionEntity
        {
            Id = recepcionId,
            ObraId = command.ObraId,
            OrdenCompraId = command.OrdenCompraId,
            RecepcionadoPorUsuarioId = command.UsuarioId,
            NumeroRemito = numeroRemito,
            FechaRecepcion = command.FechaRecepcion,
            MotivoProblema = motivoNormalizado,
            Observaciones = string.IsNullOrWhiteSpace(command.Observaciones) ? null : command.Observaciones.Trim(),
            CreatedAt = ahora
        };

        if (esEntregaConProblema && (command.Detalles == null || command.Detalles.Count == 0))
        {
            foreach (var itemOrden in ordenCompra.Items)
            {
                recepcion.Detalles.Add(new DetalleRecepcionEntity
                {
                    Id = Guid.NewGuid(),
                    RecepcionId = recepcionId,
                    ArticuloId = itemOrden.ArticuloId,
                    DescripcionArticulo = string.IsNullOrWhiteSpace(itemOrden.Descripcion)
                        ? itemOrden.ArticuloId.ToString()
                        : itemOrden.Descripcion,
                    CantidadEsperada = itemOrden.Cantidad,
                    CantidadRecibida = 0m,
                    UnidadMedida = itemOrden.UnidadMedida,
                    Conforme = false,
                    Observacion = null,
                    CreatedAt = ahora
                });
            }
        }
        else
        {
            _validator.ValidarDetalles(command.Detalles!, ordenCompra, saldosPendientes, esEntregaConProblema);

            foreach (var detalleRequest in command.Detalles!)
            {
                var articuloComprado = ordenCompra.Items
                    .First(i => i.ArticuloId == detalleRequest.ArticuloId);

                recepcion.Detalles.Add(new DetalleRecepcionEntity
                {
                    Id = Guid.NewGuid(),
                    RecepcionId = recepcionId,
                    ArticuloId = detalleRequest.ArticuloId,
                    DescripcionArticulo = detalleRequest.DescripcionArticulo,
                    CantidadEsperada = articuloComprado.Cantidad,
                    CantidadRecibida = detalleRequest.CantidadRecibida,
                    UnidadMedida = detalleRequest.UnidadMedida,
                    Conforme = detalleRequest.Conforme,
                    Observacion = detalleRequest.Observacion,
                    CreatedAt = ahora
                });
            }
        }

        var correlationId = command.CorrelationId != Guid.Empty
            ? command.CorrelationId
            : Guid.NewGuid();

        if (esEntregaConProblema)
        {
            recepcion.CambiarEstado(EstadoRecepcion.Rechazada);

            for (int i = 0; i < fotosValidadas.Count; i++)
            {
                var foto = fotosValidadas[i];
                recepcion.Fotos.Add(new FotoRecepcion
                {
                    Id = Guid.NewGuid(),
                    RecepcionId = recepcionId,
                    NombreArchivo = foto.NombreArchivo,
                    ContentType = foto.ContentType,
                    TamanoBytes = foto.Contenido.LongLength,
                    Contenido = foto.Contenido,
                    Orden = i + 1,
                    CreatedAt = ahora
                });
            }

            _recepcionRepository.AgregarRecepcion(recepcion);

            // Entrega con problema (#217): NO toca remanente ni presupuesto; publica RecepcionConProblemaEvent
            // ANTES de SaveChangesAsync para enrolar en el Outbox transaccional de MassTransit (#171).
            await _eventPublisher.PublishRecepcionConProblemaAsync(
                recepcion,
                recepcion.Fotos.Count,
                correlationId,
                ordenCompra.ProveedorId,
                cancellationToken);
        }
        else
        {
            var cantidadesTotalesOrden = ordenCompra.Items
                .ToDictionary(i => i.ArticuloId, i => i.Cantidad);

            EstadoRecepcion estadoFinal = recepcion.CalcularEstadoSegunCantidadesAcumuladas(
                cantidadesTotalesOrden,
                recibidoPrevioPorArticulo);

            recepcion.CambiarEstado(estadoFinal);

            _recepcionRepository.AgregarRecepcion(recepcion);

            if (!ordenCompra.SinPartidaPresupuestaria)
            {
                decimal montoRecibido = recepcion.Detalles.Sum(d =>
                    d.CantidadRecibida * ordenCompra.Items.First(i => i.ArticuloId == d.ArticuloId).PrecioUnitario);

                if (montoRecibido > 0)
                {
                    var partidasObra = await _partidaRepository.ObtenerPartidasPorObraParaActualizarAsync(
                        ordenCompra.ObraId,
                        cancellationToken);

                    var partida = EmparejadorPartidaPresupuestaria.BuscarPartida(partidasObra, ordenCompra.Etapa);
                    if (partida is not null)
                    {
                        partida.EjecutarMonto(montoRecibido);
                    }
                }
            }

            // El evento se publica ANTES de guardar a propósito (#171): con el outbox transaccional de MassTransit,
            // este único SaveChangesAsync guarda en la misma transacción la recepción y el evento.
            await _eventPublisher.PublishRecepcionConfirmadaAsync(recepcion, correlationId, cancellationToken);
        }

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrenciaPersistenciaException)
        {
            throw new RecepcionConflictoException("La partida presupuestaria o la orden de compra fue modificada concurrentemente. Reintentá la operación.");
        }
        catch (IndiceUnicoPersistenciaException)
        {
            throw new RecepcionConflictoException("La orden de compra ya fue recibida o el número de remito está repetido.");
        }

        _logger.LogDebug("Recepción {RecepcionId} registrada para OrdenCompra {OrdenCompraId}.", recepcion.Id, recepcion.OrdenCompraId);
        return recepcion.ToDto();
    }
}
