using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Exceptions;
using ObraSync.Recepcion.Application.UseCases.Partidas.Commands;
using ObraSync.Recepcion.Application.UseCases.Partidas.Handlers;
using ObraSync.Recepcion.Application.UseCases.Partidas.Queries;

namespace ObraSync.Recepcion.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/presupuestos")]
public class PresupuestosController : ControllerBase
{
    private readonly CrearPartidaPresupuestariaCommandHandler _crearHandler;
    private readonly ObtenerPartidasQueryHandler _obtenerPartidasHandler;
    private readonly ObtenerPartidaPorIdQueryHandler _obtenerPorIdHandler;

    public PresupuestosController(
        CrearPartidaPresupuestariaCommandHandler crearHandler,
        ObtenerPartidasQueryHandler obtenerPartidasHandler,
        ObtenerPartidaPorIdQueryHandler obtenerPorIdHandler)
    {
        _crearHandler = crearHandler ?? throw new ArgumentNullException(nameof(crearHandler));
        _obtenerPartidasHandler = obtenerPartidasHandler ?? throw new ArgumentNullException(nameof(obtenerPartidasHandler));
        _obtenerPorIdHandler = obtenerPorIdHandler ?? throw new ArgumentNullException(nameof(obtenerPorIdHandler));
    }

    /// <summary>
    /// Consulta las partidas presupuestarias, opcionalmente filtradas por ObraId y/o Etapa (#93, #154).
    /// Roles permitidos: Admin, Comprador, Capataz.
    /// </summary>
    [Authorize(Roles = "Admin,Comprador,Capataz")]
    [HttpGet("partidas")]
    public async Task<ActionResult<List<PartidaPresupuestariaDto>>> ObtenerPartidas(
        [FromQuery] Guid? obraId,
        [FromQuery] string? etapa,
        CancellationToken cancellationToken)
    {
        var result = await _obtenerPartidasHandler.Handle(new ObtenerPartidasQuery(obraId, etapa), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Obtiene el detalle de una partida presupuestaria por su identificador (#93).
    /// Roles permitidos: Admin, Comprador, Capataz.
    /// </summary>
    [Authorize(Roles = "Admin,Comprador,Capataz")]
    [HttpGet("partidas/{id:guid}")]
    public async Task<ActionResult<PartidaPresupuestariaDto>> ObtenerPartidaPorId(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _obtenerPorIdHandler.Handle(new ObtenerPartidaPorIdQuery(id), cancellationToken);
        if (result == null)
        {
            return Problem(
                statusCode: 404,
                title: "Not Found",
                detail: $"No se encontró la partida presupuestaria con id '{id}'.");
        }

        return Ok(result);
    }

    /// <summary>
    /// Crea y da de alta una partida presupuestaria para una obra y etapa (#93, #154).
    /// Roles permitidos: Admin, Comprador.
    /// </summary>
    [Authorize(Roles = "Admin,Comprador")]
    [HttpPost("partidas")]
    public async Task<IActionResult> CrearPartida(
        [FromBody] CrearPartidaPresupuestariaRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request), "El cuerpo de la solicitud no puede estar vacío.");
        }

        var command = new CrearPartidaPresupuestariaCommand(request);
        var result = await _crearHandler.Handle(command, cancellationToken);

        return CreatedAtAction(
            nameof(ObtenerPartidaPorId),
            new { id = result.Id },
            result);
    }
}
