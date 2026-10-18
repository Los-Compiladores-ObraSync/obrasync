using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Requisiciones.Application.DTOs;
using ObraSync.Requisiciones.Application.Services;

namespace ObraSync.Requisiciones.Api.Controllers
{
    [ApiController]
    [Route("api/catalogo")]
    [Authorize(Roles = "Capataz,Comprador,Admin")]
    public class CatalogoController : ControllerBase
    {
        private readonly ICatalogoService _catalogoService;

        public CatalogoController(ICatalogoService catalogoService)
        {
            _catalogoService = catalogoService;
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
            var insumos = await _catalogoService.ListarInsumosAsync(search, soloActivos, cancellationToken);
            return Ok(insumos);
        }
    }
}
