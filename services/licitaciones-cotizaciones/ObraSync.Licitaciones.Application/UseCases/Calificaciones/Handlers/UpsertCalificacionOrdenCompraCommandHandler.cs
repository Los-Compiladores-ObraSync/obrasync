using MediatR;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.UseCases.Calificaciones.Commands;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.UseCases.Calificaciones.Handlers;

public class UpsertCalificacionOrdenCompraCommandHandler
    : IRequestHandler<UpsertCalificacionOrdenCompraCommand, CalificacionOrdenCompraResponseDto>
{
    private readonly IOrdenCompraRepository _ordenCompraRepository;
    private readonly ICalificacionProveedorRepository _calificacionRepository;
    private readonly TimeProvider _timeProvider;

    public UpsertCalificacionOrdenCompraCommandHandler(
        IOrdenCompraRepository ordenCompraRepository,
        ICalificacionProveedorRepository calificacionRepository,
        TimeProvider? timeProvider = null)
    {
        _ordenCompraRepository = ordenCompraRepository ?? throw new ArgumentNullException(nameof(ordenCompraRepository));
        _calificacionRepository = calificacionRepository ?? throw new ArgumentNullException(nameof(calificacionRepository));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<CalificacionOrdenCompraResponseDto> Handle(
        UpsertCalificacionOrdenCompraCommand request,
        CancellationToken cancellationToken = default)
    {
        if (request.OrdenCompraId == Guid.Empty)
        {
            throw new ArgumentException("El identificador de la orden de compra es obligatorio.", nameof(request.OrdenCompraId));
        }

        if (request.UsuarioId == Guid.Empty)
        {
            throw new ArgumentException("El identificador del usuario que califica es obligatorio.", nameof(request.UsuarioId));
        }

        if (request.Calificacion < 1 || request.Calificacion > 5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.Calificacion),
                request.Calificacion,
                "La calificación debe ser un número entero entre 1 y 5.");
        }

        string? comentarioNormalizado = null;
        if (!string.IsNullOrWhiteSpace(request.Comentario))
        {
            comentarioNormalizado = request.Comentario.Trim();
            if (comentarioNormalizado.Length > CalificacionProveedor.MaxLongitudComentario)
            {
                throw new ArgumentException(
                    $"El comentario no puede superar los {CalificacionProveedor.MaxLongitudComentario} caracteres.",
                    nameof(request.Comentario));
            }
        }

        var ordenCompra = await _ordenCompraRepository.GetByIdAsync(request.OrdenCompraId, cancellationToken)
            ?? throw new KeyNotFoundException($"No se encontró la orden de compra con ID {request.OrdenCompraId}.");

        if (!ordenCompra.RecibidaCompleta)
        {
            throw new InvalidOperationException(
                "Solo se puede calificar una orden de compra que haya sido recibida completa.");
        }

        var ahoraUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var guardada = await _calificacionRepository.UpsertPorOrdenAsync(
            ordenCompra.OrdenCompraId,
            ordenCompra.ProveedorId,
            request.UsuarioId,
            request.Calificacion,
            comentarioNormalizado,
            ahoraUtc,
            cancellationToken);

        var resumen = await _calificacionRepository.GetResumenAsync(ordenCompra.ProveedorId, cancellationToken)
            ?? new ResumenCalificacionProveedor(ordenCompra.ProveedorId, guardada.Calificacion, 1, guardada.FechaActualizacion);

        return new CalificacionOrdenCompraResponseDto(
            OrdenCompraId: ordenCompra.OrdenCompraId,
            ProveedorId: ordenCompra.ProveedorId,
            UsuarioId: request.UsuarioId,
            Calificacion: guardada.Calificacion,
            Comentario: guardada.Comentario,
            FechaActualizacion: DateTime.SpecifyKind(guardada.FechaActualizacion, DateTimeKind.Utc),
            PromedioProveedor: resumen.Promedio,
            CantidadCalificacionesProveedor: resumen.Cantidad);
    }
}
