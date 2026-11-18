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

        var entidad = await _repository.GetByProveedorIdAsync(request.ProveedorId, cancellationToken)
            ?? throw new KeyNotFoundException($"El proveedor '{request.ProveedorId}' todavía no tiene una calificación registrada.");

        return new CalificacionProveedorResponseDto(
            entidad.ProveedorId,
            entidad.Calificacion,
            DateTime.SpecifyKind(entidad.FechaActualizacion, DateTimeKind.Utc));
    }
}
