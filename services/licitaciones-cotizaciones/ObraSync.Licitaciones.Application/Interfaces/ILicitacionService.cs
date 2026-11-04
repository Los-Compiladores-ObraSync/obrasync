using ObraSync.Licitaciones.Application.DTOs;

namespace ObraSync.Licitaciones.Application.Interfaces;

public interface ILicitacionService
{
    Task<LicitacionResponseDto> CrearAsync(CrearLicitacionRequestDto request, CancellationToken ct = default);
}