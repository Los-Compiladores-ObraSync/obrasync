using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Application.UseCases.Partidas.Commands;
using ObraSync.Recepcion.Application.UseCases.Partidas.Queries;

namespace ObraSync.Recepcion.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/presupuestos")]
public class PresupuestosController : RecepcionControllerBase
{
    private readonly ICommandHandler<CrearPartidaPresupuestariaCommand, PartidaPresupuestariaDto> _crearHandler;
    private readonly IQueryHandler<ObtenerPartidasQuery, PaginaDe<PartidaPresupuestariaDto>> _obtenerPartidasHandler;
    private readonly IQueryHandler<ObtenerPartidaPorIdQuery, PartidaPresupuestariaDto?> _obtenerPorIdHandler;

    public PresupuestosController(
        ICommandHandler<CrearPartidaPresupuestariaCommand, PartidaPresupuestariaDto> crearHandler,
        IQueryHandler<ObtenerPartidasQuery, PaginaDe<PartidaPresupuestariaDto>> obtenerPartidasHandler,
        IQueryHandler<ObtenerPartidaPorIdQuery, PartidaPresupuestariaDto?> obtenerPorIdHandler,
        IObraAsignacionValidator? obraAsignacionValidator = null)
        : base(obraAsignacionValidator)
    {
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
        if (!TryValidarPaginacion(pagina, tamano, out var p, out var errorPaginacion))
        {
            return errorPaginacion!;
        }

        // Un Capataz solo ve el presupuesto de las obras que tiene asignadas (igual que en Recepciones, #189).
        var (obrasPermitidas, errorObra) = await ResolverFiltroObrasCapatazAsync(
            obraId,
            GetCorrelationIdFromHeader(),
            cancellationToken,
            DetalleObrasAsignadasNoVerificables);

        if (errorObra is not null)
        {
            return errorObra;
        }

        var result = await _obtenerPartidasHandler.Handle(
            new ObtenerPartidasQuery(obraId, etapa, p, obrasPermitidas), cancellationToken);

        SetPaginationHeaders(result.Total, p);
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
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: $"No se encontró la partida presupuestaria con id '{id}'.");
        }

        var errorObra = await ValidarObraAsignadaACapatazAsync(
            result.ObraId,
            GetCorrelationIdFromHeader(),
            cancellationToken,
            DetalleObrasAsignadasNoVerificables);

        if (errorObra is not null)
        {
            return errorObra;
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
