using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Application.Interfaces;

namespace ObraSync.Usuarios.Api.Controllers;

[ApiController]
[Route("api/plantillas-obra")]
public class PlantillasObraController : ControllerBase
{
    private readonly IPlantillaObraService _plantillaObraService;

    public PlantillasObraController(IPlantillaObraService plantillaObraService)
    {
        _plantillaObraService = plantillaObraService ?? throw new ArgumentNullException(nameof(plantillaObraService));
    }

    /// <summary>
    /// Obtiene las plantillas de obra preconfiguradas con sus etapas ordenadas y paginación (#182).
    /// Roles: Admin, Comprador, Capataz.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Admin,Comprador,Capataz")]
    [ProducesResponseType(typeof(IEnumerable<PlantillaObraResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IEnumerable<PlantillaObraResponseDto>>> ObtenerTodas(
        [FromQuery] int pagina = 1,
        [FromQuery] int tamano = ParametrosPagina.TamanoMaximo,
        CancellationToken cancellationToken = default)
    {
        var p = new ParametrosPagina(pagina, tamano);
        var error = p.Validar();
        if (error is not null)
        {
            return Problem(detail: error, statusCode: StatusCodes.Status400BadRequest, title: "Paginación inválida");
        }

        var (resultado, total) = await _plantillaObraService.ObtenerPaginadasAsync(p, cancellationToken);

        if (Response?.Headers is not null)
        {
            Response.Headers["X-Total-Count"] = total.ToString();
            Response.Headers["X-Page"] = p.Pagina.ToString();
            Response.Headers["X-Page-Size"] = p.Tamano.ToString();
        }

        return Ok(resultado);
    }

    /// <summary>
    /// Obtiene una plantilla de obra por su Id con sus etapas ordenadas.
    /// Roles: Admin, Comprador, Capataz.
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,Comprador,Capataz")]
    [ProducesResponseType(typeof(PlantillaObraResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PlantillaObraResponseDto>> ObtenerPorId(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var resultado = await _plantillaObraService.ObtenerPorIdAsync(id, cancellationToken);
        if (resultado is null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Plantilla no encontrada",
                Detail = $"No se encontró la plantilla con id {id}",
                Status = StatusCodes.Status404NotFound
            });
        }

        return Ok(resultado);
    }

    /// <summary>
    /// Crea una nueva plantilla de obra personalizada con sus etapas ordenadas.
    /// Roles: Admin, Comprador.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin,Comprador")]
    [ProducesResponseType(typeof(PlantillaObraResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PlantillaObraResponseDto>> Crear(
        [FromBody] CrearPlantillaObraRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var responseDto = await _plantillaObraService.CrearAsync(request, cancellationToken);
        return CreatedAtAction(nameof(ObtenerPorId), new { id = responseDto.Id }, responseDto);
    }
}
