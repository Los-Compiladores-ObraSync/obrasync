using MediatR;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.Mappings;
using ObraSync.Licitaciones.Application.UseCases.Licitaciones.Commands;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.UseCases.Licitaciones.Handlers;

/// <summary>
/// Extiende el plazo de una licitación ABIERTA. Reglas: la fecha nueva tiene que ser futura y posterior a la actual
/// (acortar un plazo no es "extender": para cortar antes se usa cerrar). No toca los Magic Links ya generados:
/// cada uno tiene su propio vencimiento.
/// </summary>
public class ExtenderLicitacionCommandHandler : IRequestHandler<ExtenderLicitacionCommand, LicitacionResponseDto>
{
    private readonly ILicitacionRepository _licitacionRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public ExtenderLicitacionCommandHandler(
        ILicitacionRepository licitacionRepository,
        IUnitOfWork unitOfWork,
        TimeProvider? timeProvider = null)
    {
        _licitacionRepository = licitacionRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<LicitacionResponseDto> Handle(
        ExtenderLicitacionCommand request,
        CancellationToken cancellationToken = default)
    {
        if (request.LicitacionId == Guid.Empty)
            throw new ArgumentException("El identificador de la licitación es obligatorio.", nameof(request));

        var ahoraUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nueva = request.NuevaFechaLimiteCotizacion.ToUniversalTime();
        if (nueva <= ahoraUtc)
            throw new ArgumentException("La nueva fecha límite tiene que ser futura.", nameof(request));

        var licitacion = await _licitacionRepository.GetByIdAsync(request.LicitacionId, cancellationToken)
            ?? throw new KeyNotFoundException($"No se encontró la licitación con ID {request.LicitacionId}.");

        if (licitacion.Estado != EstadoLicitacion.Abierta)
            throw new InvalidOperationException("Solo se puede extender el plazo de una licitación abierta.");

        if (nueva <= licitacion.FechaLimiteCotizacion.ToUniversalTime())
            throw new ArgumentException("La nueva fecha límite tiene que ser posterior a la actual.", nameof(request));

        licitacion.FechaLimiteCotizacion = nueva;

        await _licitacionRepository.UpdateAsync(licitacion, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return licitacion.ToDto();
    }
}
