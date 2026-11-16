using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Application.Interfaces;
using ObraSync.Usuarios.Domain.Entities;
using ObraSync.Usuarios.Domain.Exceptions;
using ObraSync.Usuarios.Infrastructure.Data;

namespace ObraSync.Usuarios.Infrastructure.Services;

public class ObraService : IObraService
{
    private readonly AuthDbContext _context;

    public ObraService(AuthDbContext context)
    {
        _context = context;
    }

    public async Task<CrearObraResponseDto> CrearObraAsync(CrearObraDto dto)
    {
        // Validar que el capataz exista
        var capataz = await _context.Users.FindAsync(dto.CapatazId);
        if (capataz == null)
        {
            throw new ValidacionException("El ID del capataz especificado no existe.");
        }

        // Validar regla de unicidad (#110): sin duplicados entre obras activas
        var nombreNormalizado = dto.Nombre.Trim().ToLower();
        var ubicacionNormalizada = (dto.Ubicacion ?? string.Empty).Trim().ToLower();

        var duplicada = await _context.Obras
            .AnyAsync(o => o.Activa
                && o.Nombre.Trim().ToLower() == nombreNormalizado
                && (o.Ubicacion ?? string.Empty).Trim().ToLower() == ubicacionNormalizada);

        if (duplicada)
        {
            throw new ConflictoException("Ya existe una obra activa con el mismo nombre y ubicación.");
        }

        var obra = new Obra
        {
            Nombre = dto.Nombre.Trim(),
            Ubicacion = dto.Ubicacion?.Trim() ?? string.Empty,
            FechaInicio = dto.FechaInicio.HasValue && dto.FechaInicio.Value != default
                ? dto.FechaInicio.Value
                : DateTime.UtcNow,
            CapatazId = dto.CapatazId,
            Activa = true
        };

        if (dto.FasesEtapas != null && dto.FasesEtapas.Any())
        {
            foreach (var nombreEtapa in dto.FasesEtapas)
            {
                obra.Etapas.Add(new EtapaObra
                {
                    NombreEtapa = nombreEtapa,
                    Estado = "Pendiente"
                });
            }
        }

        _context.Obras.Add(obra);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            throw new ConflictoException("Ya existe una obra activa con el mismo nombre y ubicación.");
        }

        return new CrearObraResponseDto
        {
            Mensaje = "Obra creada exitosamente",
            Id = obra.Id
        };
    }

    public async Task<IEnumerable<ObraResponseDto>> ListarObrasAsync(ClaimsPrincipal? usuario = null)
    {
        var (items, _) = await ListarObrasPaginadasAsync(usuario, new ParametrosPagina());
        return items;
    }

    public async Task<(IReadOnlyList<ObraResponseDto> Items, int Total)> ListarObrasPaginadasAsync(
        ClaimsPrincipal? usuario = null,
        ParametrosPagina? paginacion = null)
    {
        var p = paginacion ?? new ParametrosPagina();

        var query = _context.Obras
            .AsNoTracking()
            .Where(o => o.Activa);

        // #155: el filtro por capataz se hace en el backend (no en la pantalla). Las obras
        // asignadas no viajan en el JWT; se resuelven acá con el CapatazId de cada obra.
        if (EsCapatazSinPrivilegiosSuperiores(usuario))
        {
            if (!TryGetUserId(usuario, out var capatazId))
            {
                throw new UnauthorizedAccessException("El token JWT no contiene un claim 'sub' válido con formato GUID.");
            }

            query = query.Where(o => o.CapatazId == capatazId);
        }

        // Total con los mismos filtros, antes de paginar y sin Include (#182)
        var total = await query.CountAsync();

        var items = await query
            .Include(o => o.Capataz)
            .Include(o => o.Etapas)
            .OrderBy(o => o.Nombre)
            .ThenBy(o => o.Id)
            .Skip(p.Saltear)
            .Take(p.Tamano)
            .Select(o => new ObraResponseDto
            {
                Id = o.Id,
                Nombre = o.Nombre,
                Ubicacion = o.Ubicacion,
                FechaInicio = o.FechaInicio,
                CapatazId = o.CapatazId,
                Capataz = o.Capataz != null ? o.Capataz.NombreCompleto : null,
                Activa = o.Activa,
                Etapas = o.Etapas.Select(e => new EtapaObraResponseDto
                {
                    Id = e.Id,
                    NombreEtapa = e.NombreEtapa,
                    Estado = e.Estado
                }).ToList()
            })
            .ToListAsync();

        return (items, total);
    }

    public async Task<ObraResponseDto> EditarObraAsync(Guid id, ActualizarObraDto dto)
    {
        var obra = await _context.Obras
            .Include(o => o.Capataz)
            .Include(o => o.Etapas)
            .FirstOrDefaultAsync(o => o.Id == id && o.Activa);

        if (obra == null)
        {
            throw new KeyNotFoundException($"No se encontró la obra activa con ID {id}.");
        }

        var capataz = await _context.Users.FindAsync(dto.CapatazId);
        if (capataz == null)
        {
            throw new ValidacionException("El ID del capataz especificado no existe.");
        }

        // Validar regla de unicidad (#110): sin duplicados entre obras activas
        var nombreNormalizado = dto.Nombre.Trim().ToLower();
        var ubicacionNormalizada = (dto.Ubicacion ?? string.Empty).Trim().ToLower();

        var duplicada = await _context.Obras
            .AnyAsync(o => o.Id != id
                && o.Activa
                && o.Nombre.Trim().ToLower() == nombreNormalizado
                && (o.Ubicacion ?? string.Empty).Trim().ToLower() == ubicacionNormalizada);

        if (duplicada)
        {
            throw new ConflictoException("Ya existe una obra activa con el mismo nombre y ubicación.");
        }

        obra.Nombre = dto.Nombre.Trim();
        obra.Ubicacion = dto.Ubicacion?.Trim() ?? string.Empty;
        if (dto.FechaInicio.HasValue && dto.FechaInicio.Value != default)
        {
            obra.FechaInicio = dto.FechaInicio.Value;
        }
        obra.CapatazId = dto.CapatazId;
        obra.Capataz = capataz;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            throw new ConflictoException("Ya existe una obra activa con el mismo nombre y ubicación.");
        }

        return new ObraResponseDto
        {
            Id = obra.Id,
            Nombre = obra.Nombre,
            Ubicacion = obra.Ubicacion,
            FechaInicio = obra.FechaInicio,
            CapatazId = obra.CapatazId,
            Capataz = capataz.NombreCompleto,
            Activa = obra.Activa,
            Etapas = obra.Etapas.Select(e => new EtapaObraResponseDto
            {
                Id = e.Id,
                NombreEtapa = e.NombreEtapa,
                Estado = e.Estado
            }).ToList()
        };
    }

    public async Task DarDeBajaObraAsync(Guid id)
    {
        var obra = await _context.Obras
            .FirstOrDefaultAsync(o => o.Id == id && o.Activa);

        if (obra == null)
        {
            throw new KeyNotFoundException($"No se encontró la obra activa con ID {id}.");
        }

        obra.Activa = false;
        await _context.SaveChangesAsync();
    }

    // Un usuario con rol Capataz que además sea Comprador o Admin conserva la vista completa.
    private static bool EsCapatazSinPrivilegiosSuperiores(ClaimsPrincipal? usuario) =>
        usuario?.IsInRole(UserRoles.Capataz) == true
        && !usuario.IsInRole(UserRoles.Comprador)
        && !usuario.IsInRole(UserRoles.Admin);

    private static bool TryGetUserId(ClaimsPrincipal? usuario, out Guid userId)
    {
        var subClaim = usuario?.FindFirstValue("sub")
            ?? usuario?.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(subClaim, out userId) && userId != Guid.Empty;
    }
}
