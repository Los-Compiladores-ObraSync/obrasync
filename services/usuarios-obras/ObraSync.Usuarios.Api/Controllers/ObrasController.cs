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
        public async Task<IActionResult> CrearObra([FromBody] CrearObraDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            // Validar que el capataz exista
            var capataz = await _context.Users.FindAsync(dto.CapatazId);
            if (capataz == null)
                return BadRequest(new { mensaje = "El ID del capataz especificado no existe." });

            // Crear la entidad Obra
            var obra = new Obra
            {
                Nombre = dto.Nombre,
                Ubicacion = dto.Ubicacion,
                FechaInicio = dto.FechaInicio.HasValue && dto.FechaInicio.Value != default
                    ? dto.FechaInicio.Value
                    : DateTime.UtcNow,
                CapatazId = dto.CapatazId
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
    }
}
