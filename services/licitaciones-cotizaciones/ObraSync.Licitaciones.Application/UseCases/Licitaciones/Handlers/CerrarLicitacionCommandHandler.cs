using MediatR;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.Mappings;
using ObraSync.Licitaciones.Application.UseCases.Licitaciones.Commands;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.UseCases.Licitaciones.Handlers;

public class CerrarLicitacionCommandHandler : IRequestHandler<CerrarLicitacionCommand, LicitacionResponseDto>
{
    public const int MaxLongitudMotivo = 500;

    private readonly ILicitacionRepository _licitacionRepository;
    private readonly IMagicLinkRepository _magicLinkRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public CerrarLicitacionCommandHandler(
        ILicitacionRepository licitacionRepository,
        IMagicLinkRepository magicLinkRepository,
        IUnitOfWork unitOfWork,
        TimeProvider? timeProvider = null)
    {
        _licitacionRepository = licitacionRepository;
        _magicLinkRepository = magicLinkRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<LicitacionResponseDto> Handle(
        CerrarLicitacionCommand request,
        CancellationToken cancellationToken = default)
    {
        if (request.LicitacionId == Guid.Empty)
        {
            throw new ArgumentException("El identificador de la licitación es obligatorio.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Motivo))
        {
            throw new ArgumentException("El motivo de cierre es obligatorio.", nameof(request));
        }

        var motivoNormalizado = request.Motivo.Trim();
        if (motivoNormalizado.Length > MaxLongitudMotivo)
        {
            throw new ArgumentException(
                $"El motivo de cierre no puede superar los {MaxLongitudMotivo} caracteres.",
                nameof(request));
        }

        var licitacion = await _licitacionRepository.GetByIdAsync(request.LicitacionId, cancellationToken)
            ?? throw new KeyNotFoundException($"No se encontró la licitación con ID {request.LicitacionId}.");

        if (licitacion.Estado != EstadoLicitacion.Abierta)
        {
            var detalleEstado = licitacion.Estado == EstadoLicitacion.Adjudicada
                ? "La licitación ya se encuentra adjudicada y no puede cerrarse."
                : "La licitación ya se encuentra cerrada.";
            throw new InvalidOperationException(detalleEstado);
        }

        var ahoraUtc = _timeProvider.GetUtcNow().UtcDateTime;

        licitacion.Estado = EstadoLicitacion.Cerrada;
        licitacion.MotivoCierre = motivoNormalizado;
        licitacion.FechaCierre = ahoraUtc;

        await _licitacionRepository.UpdateAsync(licitacion, cancellationToken);
        await _magicLinkRepository.InvalidarPendientesPorLicitacionIdAsync(licitacion.LicitacionId, ahoraUtc, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return licitacion.ToDto();
    }
}
