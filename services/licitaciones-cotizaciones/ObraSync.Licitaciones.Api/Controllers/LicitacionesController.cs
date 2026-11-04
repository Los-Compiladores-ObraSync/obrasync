using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Exceptions;
using ObraSync.Licitaciones.Application.Interfaces;

namespace ObraSync.Licitaciones.Api.Controllers;

[ApiController]
[Route("api/licitaciones")]
[Authorize]
public class LicitacionesController : ControllerBase
{
    private readonly ILicitacionService _service;

    public LicitacionesController(ILicitacionService service) => _service = service;

    /// <summary>Publicar una licitación a partir de una requisición aprobada (Roles: Comprador, Admin).</summary>
    [HttpPost]
    [Authorize(Roles = "Comprador,Admin")]
    [ProducesResponseType(typeof(LicitacionResponseDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Crear([FromBody] CrearLicitacionRequestDto request, CancellationToken ct)
    {
        try
        {
            var licitacion = await _service.CrearAsync(request, ct);
            return StatusCode(StatusCodes.Status201Created, licitacion);
        }
        catch (ValidacionException ex) { return Error(StatusCodes.Status400BadRequest, "Datos inválidos", ex.Message); }
        catch (NoEncontradoException ex) { return Error(StatusCodes.Status404NotFound, "No encontrado", ex.Message); }
        catch (ConflictoException ex) { return Error(StatusCodes.Status409Conflict, "Conflicto", ex.Message); }
    }

    // ProblemDetails + correlationId, igual que el resto de los errores del servicio
    private ObjectResult Error(int status, string title, string detail)
    {
        var problem = ProblemDetailsFactory.CreateProblemDetails(HttpContext, status, title, detail: detail);
        problem.Extensions["correlationId"] = Request.Headers["X-Correlation-ID"].ToString();
        return new ObjectResult(problem) { StatusCode = status };
    }
}