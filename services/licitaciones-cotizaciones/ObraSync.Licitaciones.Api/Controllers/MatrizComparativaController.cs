using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Licitaciones.Api.Middlewares;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.UseCases.Cotizaciones.Queries;

namespace ObraSync.Licitaciones.Api.Controllers;

[Route("api/cotizaciones")]
[Authorize]
public class MatrizComparativaController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public MatrizComparativaController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Listar las cotizaciones de un pliego para la matriz comparativa (Roles: Comprador, Admin).
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Comprador,Admin")]
    [MapExceptionProblem(typeof(KeyNotFoundException), StatusCodes.Status404NotFound, "Licitación no encontrada")]
    [MapExceptionProblem(typeof(ArgumentException), StatusCodes.Status400BadRequest, "Parámetros de consulta inválidos")]
    [ProducesResponseType(typeof(IEnumerable<CotizacionResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCotizaciones(
        [FromQuery] Guid? licitacionId,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamano = 50,
        [FromQuery] decimal? pesoPrecio = null,
        [FromQuery] decimal? pesoFlete = null,
        [FromQuery] decimal? pesoPlazo = null,
        [FromQuery] decimal? pesoCalificacion = null,
        [FromQuery] decimal? precio = null,
        [FromQuery] decimal? flete = null,
        [FromQuery] decimal? plazo = null,
        [FromQuery] decimal? calificacion = null,
        CancellationToken cancellationToken = default)
    {
        if (!licitacionId.HasValue || licitacionId.Value == Guid.Empty)
        {
            return Problem(
                detail: "El parámetro de consulta 'licitacionId' es obligatorio y debe ser un GUID válido.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Parámetro licitacionId inválido");
        }

        if (!TryCrearPaginacion(pagina, tamano, out var paginacion, out var errorResult))
        {
            return errorResult!;
        }

        var query = new GetCotizacionesByLicitacionIdQuery(
            licitacionId.Value,
            paginacion,
            PesoPrecio: pesoPrecio ?? precio,
            PesoFlete: pesoFlete ?? flete,
            PesoPlazo: pesoPlazo ?? plazo,
            PesoCalificacion: pesoCalificacion ?? calificacion);

        var resultado = await _mediator.Send(query, cancellationToken);

        EscribirHeadersPaginacion(resultado.Total, paginacion);
        return Ok(resultado.Items);
    }
}
