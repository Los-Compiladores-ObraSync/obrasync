using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Requisiciones.Application.DTOs;
using ObraSync.Requisiciones.Application.UseCases.Catalog.Handlers;
using ObraSync.Requisiciones.Application.UseCases.Catalog.Queries;

namespace ObraSync.Requisiciones.Api.Controllers
{
    [ApiController]
    [Route("api/catalogo")]
    [Authorize(Roles = "Capataz,Comprador,Admin")]
    public class CatalogoController : ControllerBase
    {
        private readonly GetInsumosQueryHandler _getInsumosHandler;

        public CatalogoController(GetInsumosQueryHandler getInsumosHandler)
        {
            _getInsumosHandler = getInsumosHandler;
        }

        /// <summary>
        /// Listar los insumos del catálogo (Roles: Capataz, Comprador, Admin)
        /// </summary>
        /// <param name="search">Filtro opcional por código o descripción del insumo.</param>
        /// <param name="soloActivos">Filtrar únicamente insumos activos (por defecto true).</param>
        /// <param name="cancellationToken">Token de cancelación.</param>
        [HttpGet("insumos")]
        [ProducesResponseType(typeof(IEnumerable<InsumoCatalogoDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<IReadOnlyList<InsumoCatalogoDto>>> ListarInsumos(
            [FromQuery] string? search = null,
            [FromQuery] bool soloActivos = true,
            CancellationToken cancellationToken = default)
        {
            var query = new GetInsumosQuery(search, soloActivos);
            var insumos = await _getInsumosHandler.Handle(query, cancellationToken);
            return Ok(insumos);
        }
    }
}
