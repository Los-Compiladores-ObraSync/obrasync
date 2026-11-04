using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.Mappings;
using ObraSync.Licitaciones.Application.UseCases.Licitaciones.Queries;

namespace ObraSync.Licitaciones.Application.UseCases.Licitaciones.Handlers;

public class GetLicitacionByIdQueryHandler
{
    private readonly ILicitacionRepository _repository;

    public GetLicitacionByIdQueryHandler(ILicitacionRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<LicitacionResponseDto> Handle(
        GetLicitacionByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        var licitacion = await _repository.GetByIdAsync(query.LicitacionId, cancellationToken);
        if (licitacion == null)
        {
            throw new KeyNotFoundException($"No se encontró la licitación con Id '{query.LicitacionId}'.");
        }

        return licitacion.ToDto();
    }
}
