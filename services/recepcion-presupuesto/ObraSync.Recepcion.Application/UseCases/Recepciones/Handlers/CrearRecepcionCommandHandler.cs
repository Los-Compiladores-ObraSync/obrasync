using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Exceptions;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Application.Mappings;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Commands;
using ObraSync.Recepcion.Domain.Enums;
using DetalleRecepcionEntity = ObraSync.Recepcion.Domain.Entities.DetalleRecepcion;
using RecepcionEntity = ObraSync.Recepcion.Domain.Entities.Recepcion;

namespace ObraSync.Recepcion.Application.UseCases.Recepciones.Handlers;

public class CrearRecepcionCommandHandler
{
    private readonly IRecepcionDbContext _context;
    private readonly IRecepcionEventPublisher _eventPublisher;
    private readonly ILogger<CrearRecepcionCommandHandler> _logger;

    public CrearRecepcionCommandHandler(
        IRecepcionDbContext context,
        IRecepcionEventPublisher eventPublisher,
        ILogger<CrearRecepcionCommandHandler> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<RecepcionDto> Handle(
        CrearRecepcionCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.UsuarioId == Guid.Empty)
        {
            throw new ArgumentException("El usuario que registra la recepción es obligatorio.");
        }

        if (command.ObraId == Guid.Empty ||
            command.OrdenCompraId == Guid.Empty)
        {
            throw new ArgumentException("La obra y la orden de compra son obligatorias.");
        }

        if (string.IsNullOrWhiteSpace(command.NumeroRemito) ||
            command.NumeroRemito.Length > 50)
        {
            throw new ArgumentException("El número de remito es obligatorio y no puede superar los 50 caracteres.");
        }

        if (command.FechaRecepcion == default)
        {
            throw new ArgumentException("La fecha de recepción es obligatoria.");
        }

        // Se permite un día de tolerancia por la diferencia horaria con UTC.
        DateTime fechaMaximaPermitida = DateTime.UtcNow.Date.AddDays(1);

        if (command.FechaRecepcion.Date > fechaMaximaPermitida)
        {
            throw new ArgumentException("La fecha de recepcion no puede ser futura.");
        }

        if (command.Observaciones?.Length > 500)
        {
            throw new ArgumentException("Las observaciones no pueden superar los 500 caracteres.");
        }

        if (command.Detalles == null || command.Detalles.Count == 0)
        {
            throw new ArgumentException("La recepción debe incluir al menos un material.");
        }

        var ordenCompra = await _context.OrdenesCompra
            .Include(o => o.Items)
            .AsNoTracking()
            .FirstOrDefaultAsync(
                o => o.Id == command.OrdenCompraId,
                cancellationToken);

        if (ordenCompra == null)
        {
            throw new ArgumentException("La orden de compra no existe.");
        }

        if (ordenCompra.ObraId != command.ObraId)
        {
            throw new ArgumentException("La orden de compra no pertenece a la obra indicada.");
        }

        if (ordenCompra.Estado == EstadoOrdenCompra.Bloqueada)
        {
            throw new RecepcionConflictoException("La orden de compra se encuentra bloqueada por desvío presupuestario y no permite recepciones.");
        }

        if (!ordenCompra.EstaDespachada)
        {
            throw new RecepcionConflictoException("La orden de compra todavía no fue despachada");
        }

        bool yaRecibida = await _context.Recepciones
            .AnyAsync(
                r => r.OrdenCompraId == command.OrdenCompraId &&
                     r.Estado == EstadoRecepcion.Completa,
                cancellationToken);

        if (yaRecibida)
        {
            throw new RecepcionConflictoException("La orden de compra ya fue recibida por completo.");
        }

        string numeroRemito = command.NumeroRemito.Trim();

        bool remitoRepetido = await _context.Recepciones
            .AnyAsync(
                r => r.OrdenCompraId == command.OrdenCompraId &&
                     r.NumeroRemito == numeroRemito,
                cancellationToken);

        if (remitoRepetido)
        {
            throw new RecepcionConflictoException("Ya existe una recepción con ese número de remito para esta orden de compra.");
        }

        if (command.Detalles.Count != ordenCompra.Items.Count)
        {
            throw new ArgumentException("La recepción debe incluir todos los materiales de la orden de compra.");
        }

        bool articulosRepetidos = command.Detalles
            .GroupBy(d => d.ArticuloId)
            .Any(grupo => grupo.Count() > 1);

        if (articulosRepetidos)
        {
            throw new ArgumentException("No se puede incluir un mismo artículo más de una vez.");
        }

        var recibidosPrevios = await _context.Recepciones
            .AsNoTracking()
            .Where(r => r.OrdenCompraId == command.OrdenCompraId &&
                        r.Estado != EstadoRecepcion.Rechazada)
            .SelectMany(r => r.Detalles.Select(d => new { d.ArticuloId, d.CantidadRecibida }))
            .GroupBy(d => d.ArticuloId)
            .Select(g => new { ArticuloId = g.Key, Total = g.Sum(d => d.CantidadRecibida) })
            .ToListAsync(cancellationToken);

        var recibidoPrevioPorArticulo = recibidosPrevios
            .ToDictionary(x => x.ArticuloId, x => x.Total);

        var recepcion = new RecepcionEntity
        {
            Id = Guid.NewGuid(),
            ObraId = command.ObraId,
            OrdenCompraId = command.OrdenCompraId,
            RecepcionadoPorUsuarioId = command.UsuarioId,
            NumeroRemito = numeroRemito,
            FechaRecepcion = command.FechaRecepcion,
            Observaciones = command.Observaciones,
            CreatedAt = DateTime.UtcNow
        };

        foreach (var detalleRequest in command.Detalles)
        {
            var articuloComprado = ordenCompra.Items
                .FirstOrDefault(i => i.ArticuloId == detalleRequest.ArticuloId);

            if (articuloComprado == null)
            {
                throw new ArgumentException("Uno de los artículos no pertenece a la orden de compra.");
            }

            decimal recibidoPrevio = recibidoPrevioPorArticulo.GetValueOrDefault(detalleRequest.ArticuloId, 0m);
            decimal cantidadPendiente = articuloComprado.Cantidad - recibidoPrevio;

            if (detalleRequest.CantidadEsperada != articuloComprado.Cantidad &&
                detalleRequest.CantidadEsperada != cantidadPendiente)
            {
                throw new ArgumentException("La cantidad esperada no coincide con la orden de compra.");
            }

            if (detalleRequest.CantidadRecibida < 0)
            {
                throw new ArgumentException("La cantidad recibida no puede ser negativa.");
            }

            if (recibidoPrevio + detalleRequest.CantidadRecibida > articuloComprado.Cantidad)
            {
                throw new ArgumentException("La cantidad recibida acumulada supera la cantidad pedida en la orden de compra.");
            }

            if (string.IsNullOrWhiteSpace(detalleRequest.DescripcionArticulo) ||
                detalleRequest.DescripcionArticulo.Length > 250 ||
                string.IsNullOrWhiteSpace(detalleRequest.UnidadMedida) ||
                detalleRequest.UnidadMedida.Length > 20 ||
                detalleRequest.Observacion?.Length > 500)
            {
                throw new ArgumentException("Los datos de uno de los materiales no son válidos.");
            }

            var detalle = new DetalleRecepcionEntity
            {
                Id = Guid.NewGuid(),
                ArticuloId = detalleRequest.ArticuloId,
                DescripcionArticulo = detalleRequest.DescripcionArticulo,
                CantidadEsperada = articuloComprado.Cantidad,
                CantidadRecibida = detalleRequest.CantidadRecibida,
                UnidadMedida = detalleRequest.UnidadMedida,
                Conforme = detalleRequest.Conforme,
                Observacion = detalleRequest.Observacion,
                CreatedAt = DateTime.UtcNow
            };

            recepcion.Detalles.Add(detalle);
        }

        if (recepcion.Detalles.Sum(d => d.CantidadRecibida) <= 0)
        {
            throw new ArgumentException("La recepción debe registrar una cantidad recibida mayor a cero en al menos un material.");
        }

        var cantidadesTotalesOrden = ordenCompra.Items
            .ToDictionary(i => i.ArticuloId, i => i.Cantidad);

        EstadoRecepcion estadoFinal = recepcion.CalcularEstadoSegunCantidadesAcumuladas(
            cantidadesTotalesOrden,
            recibidoPrevioPorArticulo);

        recepcion.CambiarEstado(estadoFinal);

        _context.Recepciones.Add(recepcion);

        var correlationId = command.CorrelationId != Guid.Empty
            ? command.CorrelationId
            : Guid.NewGuid();

        // El evento se publica ANTES de guardar a propósito (#171): con el outbox transaccional de MassTransit,
        // este único SaveChangesAsync guarda en la misma transacción la recepción y el evento.
        // Si RabbitMQ está caído, la recepción se guarda y el evento se entrega automáticamente al restablecer la conexión.
        await _eventPublisher.PublishRecepcionConfirmadaAsync(recepcion, correlationId, cancellationToken);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (EsViolacionDeIndiceUnico(ex))
        {
            throw new RecepcionConflictoException("La orden de compra ya fue recibida o el número de remito está repetido.");
        }

        return recepcion.ToDto();
    }

    private static bool EsViolacionDeIndiceUnico(DbUpdateException ex)
    {
        var inner = ex.InnerException;
        if (inner == null)
        {
            return false;
        }

        if (inner.GetType().Name == "SqlException" &&
            inner.GetType().GetProperty("Number")?.GetValue(inner) is int sqlNumber)
        {
            return sqlNumber == 2601 || sqlNumber == 2627;
        }

        return false;
    }
}
