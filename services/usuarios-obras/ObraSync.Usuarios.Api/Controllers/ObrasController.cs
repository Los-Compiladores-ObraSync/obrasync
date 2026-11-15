using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Application.Interfaces;

namespace ObraSync.Usuarios.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // Requiere estar autenticado con JWT vía Gateway
public class ObrasController : ControllerBase
{
    private readonly IObraService _obraService;

    public ObrasController(IObraService obraService)
    {
        _obraService = obraService;
    }

    /// <summary>
    /// Dar de alta una nueva obra con sus etapas constructivas (Roles: Admin, Comprador)
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin,Comprador")]
    [ProducesResponseType(typeof(CrearObraResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CrearObra([FromBody] CrearObraDto dto)
    {
        var respuesta = await _obraService.CrearObraAsync(dto);
        return CreatedAtAction(nameof(CrearObra), new { id = respuesta.Id }, respuesta);
    }

    /// <summary>
    /// Listar obras disponibles con sus etapas constructivas (Roles: Admin, Comprador, Capataz).
    /// Admin y Comprador ven todas las obras activas; el Capataz ve solo las que tiene asignadas (#155).
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Admin,Comprador,Capataz")]
    [ProducesResponseType(typeof(IEnumerable<ObraResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<ObraResponseDto>>> ListarObras()
    {
        var obras = await _obraService.ListarObrasAsync(User);
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
        var respuesta = await _obraService.EditarObraAsync(id, dto);
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
        await _obraService.DarDeBajaObraAsync(id);
        return NoContent();
    }
}
