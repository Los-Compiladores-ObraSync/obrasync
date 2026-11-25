using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Requisiciones.Application.DTOs;
using ObraSync.Requisiciones.Application.Interfaces;
using ObraSync.Requisiciones.Application.Mappings;
using ObraSync.Requisiciones.Application.UseCases.Catalog.Commands;
using ObraSync.Requisiciones.Application.UseCases.Catalog.Queries;

namespace ObraSync.Requisiciones.Api.Controllers;

[Route("api/catalogo")]
[Authorize(Roles = "Capataz,Comprador,Admin")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
public class CatalogoController : ApiControllerBase
{
    private readonly IQueryHandler<GetInsumosQuery, PaginaDe<InsumoCatalogoDto>> _getInsumosHandler;
    private readonly ICommandHandler<CreateInsumoCommand, InsumoCatalogoDto> _createInsumoHandler;

    public CatalogoController(
        IQueryHandler<GetInsumosQuery, PaginaDe<InsumoCatalogoDto>> getInsumosHandler,
        ICommandHandler<CreateInsumoCommand, InsumoCatalogoDto> createInsumoHandler)
    {
        _getInsumosHandler = getInsumosHandler;
        _createInsumoHandler = createInsumoHandler;
    }

    /// <summary>
    /// Listar los insumos del catálogo (Roles: Capataz, Comprador, Admin)
    /// </summary>
    /// <param name="search">Filtro opcional por código o descripción del insumo. Búsqueda por subcadena (contiene), insensible a mayúsculas/minúsculas pero sensible a tildes según la collation de la base de datos (SQL_Latin1_General_CP1_CI_AS).</param>
    /// <param name="soloActivos">Filtrar únicamente insumos activos (por defecto true).</param>
    /// <param name="pagina">Número de página (1 en adelante).</param>
    /// <param name="tamano">Cantidad de elementos por página (entre 1 y 50).</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    [HttpGet("insumos")]
    [ProducesResponseType(typeof(IEnumerable<InsumoCatalogoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<InsumoCatalogoDto>>> ListarInsumos(
        [FromQuery] string? search = null,
        [FromQuery] bool soloActivos = true,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamano = ParametrosPagina.TamanoMaximo,
        CancellationToken cancellationToken = default)
    {
        var paginacion = ParametrosPagina.Crear(pagina, tamano);
        var query = new GetInsumosQuery(search, soloActivos, paginacion);
        var resultado = await _getInsumosHandler.Handle(query, cancellationToken);
        return OkPaginado(resultado, paginacion);
    }

    /// <summary>
    /// Dar de alta un nuevo insumo en el catálogo (Roles: Comprador, Admin)
    /// </summary>
    /// <param name="request">Datos del nuevo insumo.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    [HttpPost("insumos")]
    [Authorize(Roles = "Admin,Comprador")]
    [ProducesResponseType(typeof(InsumoCatalogoDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InsumoCatalogoDto>> CrearInsumo(
        [FromBody] CrearInsumoRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var creado = await _createInsumoHandler.Handle(request.ToCommand(), cancellationToken);
        return CreatedAtAction(nameof(ListarInsumos), new { search = creado.Codigo }, creado);
    }
}
