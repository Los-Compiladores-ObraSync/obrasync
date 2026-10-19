using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Requisiciones.Application.DTOs;
using ObraSync.Requisiciones.Application.UseCases.Catalog.Commands;
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
        private readonly CreateInsumoCommandHandler _createInsumoHandler;

        public CatalogoController(
            GetInsumosQueryHandler getInsumosHandler,
            CreateInsumoCommandHandler createInsumoHandler)
        {
            _getInsumosHandler = getInsumosHandler;
            _createInsumoHandler = createInsumoHandler;
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

        /// <summary>
        /// Dar de alta un nuevo insumo en el catálogo (Roles: Comprador, Admin)
        /// </summary>
        /// <param name="request">Datos del nuevo insumo.</param>
        /// <param name="cancellationToken">Token de cancelación.</param>
        [HttpPost("insumos")]
        [Authorize(Roles = "Admin,Comprador")]
        [ProducesResponseType(typeof(InsumoCatalogoDto), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<ActionResult<InsumoCatalogoDto>> CrearInsumo(
            [FromBody] CrearInsumoRequestDto request,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var command = new CreateInsumoCommand(
                    request.Codigo,
                    request.Descripcion,
                    request.UnidadMedida,
                    request.Activo,
                    request.PrecioReferencia,
                    request.Moneda);

                var creado = await _createInsumoHandler.Handle(command, cancellationToken);

                return CreatedAtAction(
                    nameof(ListarInsumos),
                    new { search = creado.Codigo },
                    creado);
            }
            catch (ArgumentException ex)
            {
                return Problem(
                    detail: ex.Message,
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Datos de insumo inválidos");
            }
            catch (InvalidOperationException ex)
            {
                return Problem(
                    detail: ex.Message,
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Conflicto al crear el insumo");
            }
        }
    }
}
