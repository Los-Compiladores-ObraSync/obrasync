using MediatR;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.UseCases.Calificaciones.Queries;

namespace ObraSync.Licitaciones.Application.UseCases.Calificaciones.Handlers;

public class GetCalificacionProveedorQueryHandler
    : IRequestHandler<GetCalificacionProveedorQuery, CalificacionProveedorResponseDto>
{
    private readonly ICalificacionProveedorRepository _repository;

    public GetCalificacionProveedorQueryHandler(ICalificacionProveedorRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<CalificacionProveedorResponseDto> Handle(
        GetCalificacionProveedorQuery request,
        CancellationToken cancellationToken = default)
    {
        if (request.ProveedorId == Guid.Empty)
        {
            throw new ArgumentException("El identificador del proveedor es obligatorio.", nameof(request.ProveedorId));
        }

        var resumen = await _repository.GetResumenAsync(request.ProveedorId, cancellationToken)
            ?? throw new KeyNotFoundException($"El proveedor '{request.ProveedorId}' todavía no tiene una calificación registrada.");

        var ultimosComentarios = await _repository.GetUltimosComentariosAsync(request.ProveedorId, 20, cancellationToken);

        return new CalificacionProveedorResponseDto(
            resumen.ProveedorId,
            resumen.Promedio,
            resumen.Cantidad,
            DateTime.SpecifyKind(resumen.FechaActualizacion, DateTimeKind.Utc),
            ultimosComentarios);
    }
}
