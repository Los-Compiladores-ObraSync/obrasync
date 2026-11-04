using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.Mappings;
using ObraSync.Licitaciones.Application.UseCases.Licitaciones.Queries;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.UseCases.Licitaciones.Handlers;

public class GetLicitacionesQueryHandler
{
    private readonly ILicitacionRepository _repository;

    public GetLicitacionesQueryHandler(ILicitacionRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<IReadOnlyList<LicitacionResponseDto>> Handle(
        GetLicitacionesQuery query,
        CancellationToken cancellationToken = default)
    {
        var estadoEfectivo = string.Equals(query.RolUsuario, "Proveedor", StringComparison.OrdinalIgnoreCase)
            ? EstadoLicitacion.Abierta.ToString()
            : query.Estado;

        var licitaciones = await _repository.GetFilteredAsync(
            query.ObraId,
            estadoEfectivo,
            query.RolUsuario,
            cancellationToken);

        return licitaciones.Select(l => l.ToDto()).ToList();
    }
}
