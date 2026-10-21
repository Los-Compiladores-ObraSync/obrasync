using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Domain.Entities;
using ObraSync.Usuarios.Infrastructure.Data;
using System;
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

        [HttpPost]
        [Authorize(Roles = "Admin,Comprador")] // Solo Admin o Comprador pueden crear obras según el negocio
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

            return CreatedAtAction(nameof(CrearObra), new { id = obra.Id }, new { mensaje = "Obra creada exitosamente", obra.Id });
        }
        [HttpGet]
        [Authorize(Roles = "Admin,Comprador,Capataz")] // Roles que pueden consultar las obras
        public async Task<IActionResult> ListarObras()
        {
            var obras = await _context.Obras
                .Include(o => o.Capataz)
                .Include(o => o.Etapas)
                .Select(o => new
                {
                    o.Id,
                    o.Nombre,
                    o.Ubicacion,
                    o.FechaInicio,
                    Capataz = o.Capataz != null ? o.Capataz.NombreCompleto : null,
                    Etapas = o.Etapas.Select(e => new { e.Id, e.NombreEtapa, e.Estado })
                })
                .ToListAsync();

            return Ok(obras);
        }
    }
}