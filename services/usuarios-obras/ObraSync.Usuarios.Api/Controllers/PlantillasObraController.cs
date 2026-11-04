using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Domain.Entities;
using ObraSync.Usuarios.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ObraSync.Usuarios.Controllers
{
    [ApiController]
    [Route("api/plantillas-obra")]
    public class PlantillasObraController : ControllerBase
    {
        private readonly AuthDbContext _context;

        public PlantillasObraController(AuthDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Obtiene todas las plantillas de obra preconfiguradas con sus etapas ordenadas.
        /// Roles: Admin, Comprador, Capataz.
        /// </summary>
        [HttpGet]
        [Authorize(Roles = "Admin,Comprador,Capataz")]
        public async Task<ActionResult<IEnumerable<PlantillaObraResponseDto>>> ObtenerTodas()
        {
            var plantillas = await _context.PlantillasObra
                .Include(p => p.Etapas)
                .AsNoTracking()
                .ToListAsync();

            var resultado = plantillas.Select(p => new PlantillaObraResponseDto
            {
                Id = p.Id,
                Nombre = p.Nombre,
                Descripcion = p.Descripcion,
                Etapas = p.Etapas
                    .OrderBy(e => e.Orden)
                    .Select(e => new EtapaPlantillaDto
                    {
                        Id = e.Id,
                        NombreEtapa = e.NombreEtapa,
                        Orden = e.Orden
                    }).ToList()
            }).ToList();

            return Ok(resultado);
        }

        /// <summary>
        /// Obtiene una plantilla de obra por su Id con sus etapas ordenadas.
        /// Roles: Admin, Comprador, Capataz.
        /// </summary>
        [HttpGet("{id:guid}")]
        [Authorize(Roles = "Admin,Comprador,Capataz")]
        public async Task<ActionResult<PlantillaObraResponseDto>> ObtenerPorId(Guid id)
        {
            var plantilla = await _context.PlantillasObra
                .Include(p => p.Etapas)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id);

            if (plantilla == null)
            {
                return NotFound(new ProblemDetails
                {
                    Title = "Plantilla no encontrada",
                    Detail = $"No se encontró la plantilla con id {id}",
                    Status = 404
                });
            }

            var resultado = new PlantillaObraResponseDto
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
                    }).ToList()
            };

            return Ok(resultado);
        }

        /// <summary>
        /// Crea una nueva plantilla de obra personalizada con sus etapas ordenadas.
        /// Roles: Admin, Comprador.
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Admin,Comprador")]
        public async Task<ActionResult<PlantillaObraResponseDto>> Crear([FromBody] CrearPlantillaObraRequestDto request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var nuevaPlantilla = new PlantillaObra
            {
                Nombre = request.Nombre,
                Descripcion = request.Descripcion
            };

            if (request.Etapas != null && request.Etapas.Any())
            {
                int orden = 1;
                foreach (var nombreEtapa in request.Etapas)
                {
                    nuevaPlantilla.Etapas.Add(new EtapaPlantilla
                    {
                        NombreEtapa = nombreEtapa,
                        Orden = orden++
                    });
                }
            }

            _context.PlantillasObra.Add(nuevaPlantilla);
            await _context.SaveChangesAsync();

            var responseDto = new PlantillaObraResponseDto
            {
                Id = nuevaPlantilla.Id,
                Nombre = nuevaPlantilla.Nombre,
                Descripcion = nuevaPlantilla.Descripcion,
                Etapas = nuevaPlantilla.Etapas
                    .OrderBy(e => e.Orden)
                    .Select(e => new EtapaPlantillaDto
                    {
                        Id = e.Id,
                        NombreEtapa = e.NombreEtapa,
                        Orden = e.Orden
                    }).ToList()
            };

            return CreatedAtAction(nameof(ObtenerPorId), new { id = responseDto.Id }, responseDto);
        }
    }
}