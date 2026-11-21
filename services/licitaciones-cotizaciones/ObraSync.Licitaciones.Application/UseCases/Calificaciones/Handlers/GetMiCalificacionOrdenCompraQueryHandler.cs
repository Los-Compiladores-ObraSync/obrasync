using MediatR;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.UseCases.Calificaciones.Queries;

namespace ObraSync.Licitaciones.Application.UseCases.Calificaciones.Handlers;

public class GetMiCalificacionOrdenCompraQueryHandler
    : IRequestHandler<GetMiCalificacionOrdenCompraQuery, CalificacionOrdenCompraResponseDto>
{
    private readonly IOrdenCompraRepository _ordenCompraRepository;
    private readonly ICalificacionProveedorRepository _calificacionRepository;

    public GetMiCalificacionOrdenCompraQueryHandler(
        IOrdenCompraRepository ordenCompraRepository,
        ICalificacionProveedorRepository calificacionRepository)
    {
        _ordenCompraRepository = ordenCompraRepository ?? throw new ArgumentNullException(nameof(ordenCompraRepository));
        _calificacionRepository = calificacionRepository ?? throw new ArgumentNullException(nameof(calificacionRepository));
    }

    public async Task<CalificacionOrdenCompraResponseDto> Handle(
        GetMiCalificacionOrdenCompraQuery request,
        CancellationToken cancellationToken = default)
    {
        if (request.OrdenCompraId == Guid.Empty)
        {
            throw new ArgumentException("El identificador de la orden de compra es obligatorio.", nameof(request.OrdenCompraId));
        }

        if (request.UsuarioId == Guid.Empty)
        {
            throw new ArgumentException("El identificador del usuario es obligatorio.", nameof(request.UsuarioId));
        }

        var ordenCompra = await _ordenCompraRepository.GetByIdAsync(request.OrdenCompraId, cancellationToken)
            ?? throw new KeyNotFoundException($"No se encontró la orden de compra con ID {request.OrdenCompraId}.");

        var calificacion = await _calificacionRepository.GetByOrdenYUsuarioAsync(
                request.OrdenCompraId,
                request.UsuarioId,
                cancellationToken)
            ?? throw new KeyNotFoundException($"Todavía no registraste una calificación para la orden de compra {request.OrdenCompraId}.");

        var resumen = await _calificacionRepository.GetResumenAsync(ordenCompra.ProveedorId, cancellationToken)
            ?? new ResumenCalificacionProveedor(ordenCompra.ProveedorId, calificacion.Calificacion, 1, calificacion.FechaActualizacion);

        return new CalificacionOrdenCompraResponseDto(
            OrdenCompraId: ordenCompra.OrdenCompraId,
            ProveedorId: ordenCompra.ProveedorId,
            UsuarioId: request.UsuarioId,
            Calificacion: calificacion.Calificacion,
            Comentario: calificacion.Comentario,
            FechaActualizacion: DateTime.SpecifyKind(calificacion.FechaActualizacion, DateTimeKind.Utc),
            PromedioProveedor: resumen.Promedio,
            CantidadCalificacionesProveedor: resumen.Cantidad);
    }
}
