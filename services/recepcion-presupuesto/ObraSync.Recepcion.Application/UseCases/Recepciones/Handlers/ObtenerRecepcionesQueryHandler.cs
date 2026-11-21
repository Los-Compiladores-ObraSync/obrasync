using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Mappings;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Queries;

namespace ObraSync.Recepcion.Application.UseCases.Recepciones.Handlers;

public class ObtenerRecepcionesQueryHandler
{
    private readonly IRecepcionDbContext _context;

    public ObtenerRecepcionesQueryHandler(IRecepcionDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<PaginaDe<RecepcionDto>> Handle(
        ObtenerRecepcionesQuery query,
        CancellationToken cancellationToken = default)
    {
        var dbQuery = _context.Recepciones.AsNoTracking();

        if (query.ObraId.HasValue)
        {
            dbQuery = dbQuery.Where(r => r.ObraId == query.ObraId.Value);
        }
        else if (query.ObrasPermitidas != null)
        {
            dbQuery = dbQuery.Where(r => query.ObrasPermitidas.Contains(r.ObraId));
        }

        if (query.OrdenCompraId.HasValue)
        {
            dbQuery = dbQuery.Where(r => r.OrdenCompraId == query.OrdenCompraId.Value);
        }

        // Total con los mismos filtros sin Include ni Skip/Take (#185)
        var total = await dbQuery.CountAsync(cancellationToken);

        var p = query.Paginacion ?? new ParametrosPagina();
        var recepciones = await dbQuery
            .Include(r => r.Detalles)
            .OrderByDescending(r => r.FechaRecepcion)
            .ThenByDescending(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .Skip(p.Saltear)
            .Take(p.Tamano)
            .ToListAsync(cancellationToken);

        var recepcionIds = recepciones.Select(r => r.Id).ToList();

        // Proyección liviana de metadatos de fotos sin traer varbinary(max) (#217)
        var fotosMetadatos = recepcionIds.Count == 0
            ? new List<FotoMetadatoRow>()
            : await _context.FotosRecepcion
                .AsNoTracking()
                .Where(f => recepcionIds.Contains(f.RecepcionId))
                .OrderBy(f => f.Orden)
                .ThenBy(f => f.CreatedAt)
                .Select(f => new FotoMetadatoRow(
                    f.Id,
                    f.RecepcionId,
                    f.NombreArchivo,
                    f.ContentType,
                    f.TamanoBytes,
                    f.Orden))
                .ToListAsync(cancellationToken);

        var fotosPorRecepcion = fotosMetadatos
            .GroupBy(f => f.RecepcionId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<FotoRecepcionDto>)g
                    .Select(f => RecepcionMappings.CrearFotoDto(
                        f.Id, f.RecepcionId, f.NombreArchivo, f.ContentType, f.TamanoBytes, f.Orden))
                    .ToList());

        var dtos = recepciones
            .Select(r => r.ToDto(
                fotosPorRecepcion.TryGetValue(r.Id, out var fotos) ? fotos : Array.Empty<FotoRecepcionDto>()))
            .ToList();

        return new PaginaDe<RecepcionDto>(dtos, total);
    }

    private sealed record FotoMetadatoRow(
        Guid Id,
        Guid RecepcionId,
        string NombreArchivo,
        string ContentType,
        long TamanoBytes,
        int Orden);
}
