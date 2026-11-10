using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.Mappings;
using ObraSync.Licitaciones.Application.UseCases.Licitaciones.Queries;
using ObraSync.Licitaciones.Domain.Entities;

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
        // Un Proveedor solo ve pliegos Abiertos (igual que en el listado). Si no lo está, respondemos
        // lo mismo que si no existiera (404), para no revelar que ese Id existe (#136).
        var esProveedor = string.Equals(query.RolUsuario, "Proveedor", StringComparison.OrdinalIgnoreCase);
        if (licitacion == null || (esProveedor && licitacion.Estado != EstadoLicitacion.Abierta))
        {
            throw new KeyNotFoundException($"No se encontró la licitación con Id '{query.LicitacionId}'.");
        }

        return licitacion.ToDto();
    }
}
