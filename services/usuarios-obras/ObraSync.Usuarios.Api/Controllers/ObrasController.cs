using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Domain.Entities;
using ObraSync.Usuarios.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ObraSync.Usuarios.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // Requiere estar autenticado con JWT vía Gateway
    public class ObrasController : ControllerBase
    {
        private readonly AuthDbContext _context;

        public ObrasController(AuthDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Dar de alta una nueva obra con sus etapas constructivas (Roles: Admin, Comprador)
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Admin,Comprador")] // Solo Admin o Comprador pueden crear obras según el negocio
        [ProducesResponseType(typeof(CrearObraResponseDto), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<IActionResult> CrearObra([FromBody] CrearObraDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            // Validar que el capataz exista
            var capataz = await _context.Users.FindAsync(dto.CapatazId);
            if (capataz == null)
                return BadRequest(new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Capataz no encontrado",
                    Detail = "El ID del capataz especificado no existe."
                });

            // Validar regla de unicidad (#110): sin duplicados entre obras activas
            var nombreNormalizado = dto.Nombre.Trim().ToLower();
            var ubicacionNormalizada = (dto.Ubicacion ?? string.Empty).Trim().ToLower();

            var duplicada = await _context.Obras
                .AnyAsync(o => o.Activa
                    && o.Nombre.Trim().ToLower() == nombreNormalizado
                    && (o.Ubicacion ?? string.Empty).Trim().ToLower() == ubicacionNormalizada);

            if (duplicada)
            {
                return Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Obra duplicada",
                    detail: "Ya existe una obra activa con el mismo nombre y ubicación.");
            }

            // Crear la entidad Obra
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

            // Mapear las etapas constructivas
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
            await _context.SaveChangesAsync();

            var respuesta = new CrearObraResponseDto
            {
                Mensaje = "Obra creada exitosamente",
                Id = obra.Id
            };

            return CreatedAtAction(nameof(CrearObra), new { id = obra.Id }, respuesta);
        }

        /// <summary>
        /// Listar obras disponibles con sus etapas constructivas (Roles: Admin, Comprador, Capataz)
        /// </summary>
        [HttpGet]
        [Authorize(Roles = "Admin,Comprador,Capataz")] // Roles que pueden consultar las obras
        [ProducesResponseType(typeof(IEnumerable<ObraResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<IEnumerable<ObraResponseDto>>> ListarObras()
        {
            var obras = await _context.Obras
                .AsNoTracking()
                .Where(o => o.Activa)
                .Include(o => o.Capataz)
                .Include(o => o.Etapas)
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

            return Ok(obras);
        }

        /// <summary>
        /// Editar los datos de una obra existente (Roles: Admin, Comprador)
        /// </summary>
        [HttpPut("{id:guid}")]
        [Authorize(Roles = "Admin,Comprador")]
        [ProducesResponseType(typeof(ObraResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<IActionResult> EditarObra(Guid id, [FromBody] ActualizarObraDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var obra = await _context.Obras
                .Include(o => o.Capataz)
                .Include(o => o.Etapas)
                .FirstOrDefaultAsync(o => o.Id == id && o.Activa);

            if (obra == null)
            {
                return Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Obra no encontrada",
                    detail: $"No se encontró la obra activa con ID {id}.");
            }

            // Validar que el nuevo capataz exista
            var capataz = await _context.Users.FindAsync(dto.CapatazId);
            if (capataz == null)
            {
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Capataz no encontrado",
                    detail: "El ID del capataz especificado no existe.");
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
                return Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Obra duplicada",
                    detail: "Ya existe una obra activa con el mismo nombre y ubicación.");
            }

            obra.Nombre = dto.Nombre.Trim();
            obra.Ubicacion = dto.Ubicacion?.Trim() ?? string.Empty;
            if (dto.FechaInicio.HasValue && dto.FechaInicio.Value != default)
            {
                obra.FechaInicio = dto.FechaInicio.Value;
            }
            obra.CapatazId = dto.CapatazId;
            obra.Capataz = capataz;

            await _context.SaveChangesAsync();

            var respuesta = new ObraResponseDto
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

            return Ok(respuesta);
        }

        /// <summary>
        /// Dar de baja lógica una obra existente (Roles: Admin, Comprador)
        /// </summary>
        [HttpDelete("{id:guid}")]
        [Authorize(Roles = "Admin,Comprador")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DarDeBajaObra(Guid id)
        {
            var obra = await _context.Obras
                .FirstOrDefaultAsync(o => o.Id == id && o.Activa);

            if (obra == null)
            {
                return Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Obra no encontrada",
                    detail: $"No se encontró la obra activa con ID {id}.");
            }

            obra.Activa = false;
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}
