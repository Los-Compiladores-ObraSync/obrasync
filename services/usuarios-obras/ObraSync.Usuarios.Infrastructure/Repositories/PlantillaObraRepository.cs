using Microsoft.EntityFrameworkCore;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Application.Interfaces;
using ObraSync.Usuarios.Domain.Entities;
using ObraSync.Usuarios.Infrastructure.Data;

namespace ObraSync.Usuarios.Infrastructure.Repositories;

/// <summary>
/// Implementación EF Core del repositorio de plantillas de obra y sus etapas constructivas.
/// </summary>
public class PlantillaObraRepository : IPlantillaObraRepository
{
    private readonly AuthDbContext _db;

    public PlantillaObraRepository(AuthDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<(IReadOnlyList<PlantillaObra> Items, int Total)> ObtenerPaginadasAsync(
        ParametrosPagina paginacion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paginacion);

        var error = paginacion.Validar();
        if (error is not null)
        {
            throw new ArgumentOutOfRangeException(nameof(paginacion), error);
        }

        var consulta = _db.PlantillasObra.AsNoTracking();
        var total = await consulta.CountAsync(cancellationToken);

        if (total == 0 || paginacion.Saltear >= total)
        {
            return (Array.Empty<PlantillaObra>(), total);
        }

        var items = await consulta
            .Include(pl => pl.Etapas)
            .OrderBy(pl => pl.Nombre)
            .ThenBy(pl => pl.Id)
            .Skip(paginacion.Saltear)
            .Take(paginacion.Tamano)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public Task<PlantillaObra?> ObtenerPorIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return _db.PlantillasObra
            .Include(p => p.Etapas)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<PlantillaObra> CrearAsync(
        PlantillaObra plantilla,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plantilla);

        _db.PlantillasObra.Add(plantilla);
        await _db.SaveChangesAsync(cancellationToken);
        return plantilla;
    }
}
