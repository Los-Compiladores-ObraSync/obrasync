using MediatR;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.UseCases.Calificaciones.Commands;

namespace ObraSync.Licitaciones.Application.UseCases.Calificaciones.Handlers;

public class UpsertCalificacionProveedorCommandHandler
    : IRequestHandler<UpsertCalificacionProveedorCommand, CalificacionProveedorResponseDto>
{
    private readonly ICalificacionProveedorRepository _repository;
    private readonly TimeProvider _timeProvider;

    public UpsertCalificacionProveedorCommandHandler(
        ICalificacionProveedorRepository repository,
        TimeProvider? timeProvider = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<CalificacionProveedorResponseDto> Handle(
        UpsertCalificacionProveedorCommand request,
        CancellationToken cancellationToken = default)
    {
        if (request.ProveedorId == Guid.Empty)
        {
            throw new ArgumentException("El identificador del proveedor es obligatorio.", nameof(request.ProveedorId));
        }

        if (request.Calificacion < 1 || request.Calificacion > 5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.Calificacion),
                request.Calificacion,
                "La calificación del proveedor debe estar entre 1 y 5.");
        }

        var ahoraUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var entidad = await _repository.UpsertAsync(
            request.ProveedorId,
            request.Calificacion,
            ahoraUtc,
            cancellationToken);

        return new CalificacionProveedorResponseDto(
            entidad.ProveedorId,
            entidad.Calificacion,
            DateTime.SpecifyKind(entidad.FechaActualizacion, DateTimeKind.Utc));
    }
}
