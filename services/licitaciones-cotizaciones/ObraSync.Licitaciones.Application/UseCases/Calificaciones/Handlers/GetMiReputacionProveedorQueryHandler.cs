using MediatR;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.UseCases.Calificaciones.Queries;

namespace ObraSync.Licitaciones.Application.UseCases.Calificaciones.Handlers;

public class GetMiReputacionProveedorQueryHandler
    : IRequestHandler<GetMiReputacionProveedorQuery, ReputacionProveedorResponseDto>
{
    private readonly ICalificacionProveedorRepository _repository;

    public GetMiReputacionProveedorQueryHandler(ICalificacionProveedorRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<ReputacionProveedorResponseDto> Handle(
        GetMiReputacionProveedorQuery request,
        CancellationToken cancellationToken = default)
    {
        if (request.ProveedorId == Guid.Empty)
        {
            throw new ArgumentException("El identificador del proveedor es obligatorio.", nameof(request.ProveedorId));
        }

        var resumen = await _repository.GetResumenAsync(request.ProveedorId, cancellationToken);
        if (resumen is null)
        {
            return new ReputacionProveedorResponseDto(
                ProveedorId: request.ProveedorId,
                Calificacion: null,
                CantidadCalificaciones: 0,
                FechaActualizacion: null,
                UltimosComentarios: Array.Empty<ComentarioCalificacionProveedorDto>());
        }

        var ultimosComentarios = await _repository.GetUltimosComentariosAsync(request.ProveedorId, 20, cancellationToken);

        return new ReputacionProveedorResponseDto(
            ProveedorId: resumen.ProveedorId,
            Calificacion: resumen.Promedio,
            CantidadCalificaciones: resumen.Cantidad,
            FechaActualizacion: DateTime.SpecifyKind(resumen.FechaActualizacion, DateTimeKind.Utc),
            UltimosComentarios: ultimosComentarios);
    }
}
