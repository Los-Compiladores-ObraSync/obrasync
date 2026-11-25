using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Application.Interfaces;
using ObraSync.Usuarios.Domain.Entities;
using ObraSync.Usuarios.Domain.Exceptions;

namespace ObraSync.Usuarios.Application.Services;

public class PlantillaObraService : IPlantillaObraService
{
    private readonly IPlantillaObraRepository _repository;

    public PlantillaObraService(IPlantillaObraRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<(IReadOnlyList<PlantillaObraResponseDto> Items, int Total)> ObtenerPaginadasAsync(
        ParametrosPagina? paginacion = null,
        CancellationToken cancellationToken = default)
    {
        var p = paginacion ?? new ParametrosPagina();
        var error = p.Validar();
        if (error is not null)
        {
            throw new ValidacionException(error);
        }

        var (plantillas, total) = await _repository.ObtenerPaginadasAsync(p, cancellationToken);
        var resultado = plantillas.Select(MapToDto).ToList();
        return (resultado, total);
    }

    public async Task<PlantillaObraResponseDto?> ObtenerPorIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var plantilla = await _repository.ObtenerPorIdAsync(id, cancellationToken);
        return plantilla is null ? null : MapToDto(plantilla);
    }

    public async Task<PlantillaObraResponseDto> CrearAsync(
        CrearPlantillaObraRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Nombre))
        {
            throw new ValidacionException("El nombre de la plantilla es obligatorio");
        }

        var nuevaPlantilla = new PlantillaObra
        {
            Nombre = request.Nombre,
            Descripcion = request.Descripcion ?? string.Empty
        };

        if (request.Etapas is { Count: > 0 })
        {
            var orden = 1;
            foreach (var nombreEtapa in request.Etapas)
            {
                nuevaPlantilla.Etapas.Add(new EtapaPlantilla
                {
                    NombreEtapa = nombreEtapa,
                    Orden = orden++
                });
            }
        }

        var creada = await _repository.CrearAsync(nuevaPlantilla, cancellationToken);
        return MapToDto(creada);
    }

    private static PlantillaObraResponseDto MapToDto(PlantillaObra plantilla) =>
        new()
        {
            Id = plantilla.Id,
            Nombre = plantilla.Nombre,
            Descripcion = plantilla.Descripcion,
            Etapas = plantilla.Etapas
                .OrderBy(e => e.Orden)
                .Select(e => new EtapaPlantillaDto
                {
                    Id = e.Id,
                    NombreEtapa = e.NombreEtapa,
                    Orden = e.Orden
                })
                .ToList()
        };
}
