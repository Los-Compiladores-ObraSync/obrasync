using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Exceptions;
using ObraSync.Recepcion.Application.Interfaces;
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
    private readonly IObraAsignacionValidator? _obraAsignacionValidator;

    public PresupuestosController(
        CrearPartidaPresupuestariaCommandHandler crearHandler,
        ObtenerPartidasQueryHandler obtenerPartidasHandler,
        ObtenerPartidaPorIdQueryHandler obtenerPorIdHandler,
        IObraAsignacionValidator? obraAsignacionValidator = null)
    {
        _obraAsignacionValidator = obraAsignacionValidator;
        _crearHandler = crearHandler ?? throw new ArgumentNullException(nameof(crearHandler));
        _obtenerPartidasHandler = obtenerPartidasHandler ?? throw new ArgumentNullException(nameof(obtenerPartidasHandler));
        _obtenerPorIdHandler = obtenerPorIdHandler ?? throw new ArgumentNullException(nameof(obtenerPorIdHandler));
    }

    /// <summary>
    /// Consulta las partidas presupuestarias, opcionalmente filtradas por ObraId y/o Etapa (#93, #154).
    /// Soporta paginación con pagina y tamano (#185).
    /// Roles permitidos: Admin, Comprador, Capataz.
    /// </summary>
    [Authorize(Roles = "Admin,Comprador,Capataz")]
    [HttpGet("partidas")]
    public async Task<ActionResult<List<PartidaPresupuestariaDto>>> ObtenerPartidas(
        [FromQuery] Guid? obraId = null,
        [FromQuery] string? etapa = null,
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

        // Un Capataz solo ve el presupuesto de las obras que tiene asignadas (igual que en Recepciones, #189).
        IReadOnlyList<Guid>? obrasPermitidas = null;
        if (EsCapatazSinPrivilegiosSuperiores() && _obraAsignacionValidator != null)
        {
            try
            {
                if (obraId.HasValue)
                {
                    var asignada = await _obraAsignacionValidator.EstaAsignadaACapatazAsync(
                        obraId.Value, ObtenerAuthorization(), ObtenerCorrelationId(), cancellationToken);
                    if (!asignada)
                        return ObraNoAsignada();
                }
                else
                {
                    obrasPermitidas = await _obraAsignacionValidator.ObtenerObrasAsignadasAsync(
                        ObtenerAuthorization(), ObtenerCorrelationId(), cancellationToken);
                }
            }
            catch (ObraAsignacionNoVerificableException)
            {
                return ValidacionDeObraNoDisponible();
            }
        }

        var result = await _obtenerPartidasHandler.Handle(
            new ObtenerPartidasQuery(obraId, etapa, p, obrasPermitidas), cancellationToken);

        if (Response?.Headers is not null)
        {
            Response.Headers["X-Total-Count"] = result.Total.ToString();
            Response.Headers["X-Page"] = p.Pagina.ToString();
            Response.Headers["X-Page-Size"] = p.Tamano.ToString();
        }

        return Ok(result.Items.ToList());
    }

    /// <summary>
    /// Sobrecarga auxiliar para compatibilidad con invocaciones directas en pruebas unitarias.
    /// </summary>
    [NonAction]
    public Task<ActionResult<List<PartidaPresupuestariaDto>>> ObtenerPartidas(
        Guid? obraId,
        string? etapa,
        CancellationToken cancellationToken) =>
        ObtenerPartidas(obraId, etapa, 1, ParametrosPagina.TamanoMaximo, cancellationToken);

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

        if (EsCapatazSinPrivilegiosSuperiores() && _obraAsignacionValidator != null)
        {
            try
            {
                var asignada = await _obraAsignacionValidator.EstaAsignadaACapatazAsync(
                    result.ObraId, ObtenerAuthorization(), ObtenerCorrelationId(), cancellationToken);
                if (!asignada)
                    return ObraNoAsignada();
            }
            catch (ObraAsignacionNoVerificableException)
            {
                return ValidacionDeObraNoDisponible();
            }
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

    private bool EsCapatazSinPrivilegiosSuperiores() =>
        User?.Identity?.IsAuthenticated == true
        && User.IsInRole("Capataz")
        && !User.IsInRole("Admin")
        && !User.IsInRole("Comprador");

    private string? ObtenerAuthorization() => Request?.Headers["Authorization"].ToString();

    private string ObtenerCorrelationId()
    {
        var header = Request?.Headers["X-Correlation-ID"].ToString();
        return Guid.TryParse(header, out var id) && id != Guid.Empty ? id.ToString() : Guid.NewGuid().ToString();
    }

    private ObjectResult ObraNoAsignada() => Problem(
        detail: "La obra no está asignada al capataz autenticado.",
        statusCode: StatusCodes.Status403Forbidden,
        title: "Obra no asignada");

    private ObjectResult ValidacionDeObraNoDisponible() => Problem(
        detail: "No se pudo verificar las obras asignadas al capataz porque el servicio Usuarios y Obras no respondió. Reintentá en unos segundos.",
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Servicio no disponible");
}
