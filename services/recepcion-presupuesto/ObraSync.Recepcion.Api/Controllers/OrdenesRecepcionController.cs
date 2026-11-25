using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Commands;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Queries;

namespace ObraSync.Recepcion.Api.Controllers;

/// <summary>
/// Controlador enfocado en la gestión de órdenes de compra dentro del dominio de Recepción y Presupuesto:
/// órdenes pendientes de recibir, órdenes bloqueadas por desvío presupuestario, liberación administrativa y liquidación (SRP / DIP).
/// Conserva exactamente el prefijo <c>api/recepciones</c> y las rutas originales.
/// </summary>
[Authorize]
[ApiController]
[Route("api/recepciones")]
public class OrdenesRecepcionController : RecepcionControllerBase
{
    private readonly IQueryHandler<ObtenerOrdenesPendientesQuery, PaginaDe<OrdenPendienteDto>> _obtenerOrdenesPendientesHandler;
    private readonly IQueryHandler<ObtenerOrdenesBloqueadasQuery, PaginaDe<OrdenBloqueadaDto>> _obtenerOrdenesBloqueadasHandler;
    private readonly ICommandHandler<LiberarOrdenCompraCommand, OrdenCompraLiberadaDto> _liberarOrdenHandler;
    private readonly ICommandHandler<LiquidarOrdenCompraCommand, OrdenCompraLiquidadaDto> _liquidarOrdenHandler;

    public OrdenesRecepcionController(
        IQueryHandler<ObtenerOrdenesPendientesQuery, PaginaDe<OrdenPendienteDto>> obtenerOrdenesPendientesHandler,
        IQueryHandler<ObtenerOrdenesBloqueadasQuery, PaginaDe<OrdenBloqueadaDto>> obtenerOrdenesBloqueadasHandler,
        ICommandHandler<LiberarOrdenCompraCommand, OrdenCompraLiberadaDto> liberarOrdenHandler,
        ICommandHandler<LiquidarOrdenCompraCommand, OrdenCompraLiquidadaDto> liquidarOrdenHandler,
        IObraAsignacionValidator? obraAsignacionValidator = null)
        : base(obraAsignacionValidator)
    {
        _obtenerOrdenesPendientesHandler = obtenerOrdenesPendientesHandler ?? throw new ArgumentNullException(nameof(obtenerOrdenesPendientesHandler));
        _obtenerOrdenesBloqueadasHandler = obtenerOrdenesBloqueadasHandler ?? throw new ArgumentNullException(nameof(obtenerOrdenesBloqueadasHandler));
        _liberarOrdenHandler = liberarOrdenHandler ?? throw new ArgumentNullException(nameof(liberarOrdenHandler));
        _liquidarOrdenHandler = liquidarOrdenHandler ?? throw new ArgumentNullException(nameof(liquidarOrdenHandler));
    }

    /// <summary>
    /// Obtiene las órdenes de compra pendientes de recibir (sin recepción completa),
    /// opcionalmente filtradas por ObraId y/o por estado SinPartidaPresupuestaria (#196).
    /// Soporta paginación con pagina y tamano (#185).
    /// Roles permitidos: Capataz, Comprador, Admin (#189: Capataz solo puede consultar órdenes de sus obras asignadas).
    /// </summary>
    [Authorize(Roles = "Capataz,Comprador,Admin")]
    [HttpGet("ordenes-pendientes")]
    public async Task<IActionResult> ObtenerOrdenesPendientes(
        [FromQuery] Guid? obraId = null,
        [FromQuery] bool? sinPartida = null,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamano = ParametrosPagina.TamanoMaximo,
        CancellationToken cancellationToken = default)
    {
        if (!TryValidarPaginacion(pagina, tamano, out var p, out var errorPaginacion))
        {
            return errorPaginacion!;
        }

        var correlationId = GetCorrelationIdFromHeader();
        var (obrasAsignadas, errorObra) = await ResolverFiltroObrasCapatazAsync(obraId, correlationId, cancellationToken);
        if (errorObra is not null)
        {
            return errorObra;
        }

        var query = new ObtenerOrdenesPendientesQuery(obraId, sinPartida, obrasAsignadas, p);
        var resultado = await _obtenerOrdenesPendientesHandler.Handle(query, cancellationToken);
        SetPaginationHeaders(resultado.Total, p);
        return Ok(resultado.Items.ToList());
    }

    /// <summary>
    /// Sobrecarga auxiliar para compatibilidad binaria y tests unitarios directos sin parámetro sinPartida o paginación.
    /// </summary>
    [NonAction]
    public Task<IActionResult> ObtenerOrdenesPendientes(
        Guid? obraId,
        CancellationToken cancellationToken) =>
        ObtenerOrdenesPendientes(obraId, null, 1, ParametrosPagina.TamanoMaximo, cancellationToken);

    /// <summary>
    /// Sobrecarga auxiliar para compatibilidad binaria y tests unitarios directos con sinPartida sin paginación.
    /// </summary>
    [NonAction]
    public Task<IActionResult> ObtenerOrdenesPendientes(
        Guid? obraId,
        bool? sinPartida,
        CancellationToken cancellationToken) =>
        ObtenerOrdenesPendientes(obraId, sinPartida, 1, ParametrosPagina.TamanoMaximo, cancellationToken);

    /// <summary>
    /// Obtiene las órdenes de compra actualmente bloqueadas por desvío presupuestario (#210).
    /// Soporta filtro opcional por ObraId y paginación con pagina y tamano (#185).
    /// Roles permitidos: Comprador, Admin.
    /// </summary>
    [Authorize(Roles = "Comprador,Admin")]
    [HttpGet("ordenes-bloqueadas")]
    public async Task<IActionResult> ObtenerOrdenesBloqueadas(
        [FromQuery] Guid? obraId = null,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamano = ParametrosPagina.TamanoMaximo,
        CancellationToken cancellationToken = default)
    {
        if (!TryValidarPaginacion(pagina, tamano, out var p, out var errorPaginacion))
        {
            return errorPaginacion!;
        }

        var resultado = await _obtenerOrdenesBloqueadasHandler.Handle(
            new ObtenerOrdenesBloqueadasQuery(obraId, p),
            cancellationToken);

        SetPaginationHeaders(resultado.Total, p);
        return Ok(resultado.Items.ToList());
    }

    /// <summary>
    /// Registra la liberación de una orden de compra bloqueada por desvío presupuestario (#96, #154).
    /// Exclusivo para el rol Admin. Registra quién (sub del token) y cuándo la liberó,
    /// y suma su monto a MontoComprometido de la partida correspondiente.
    /// POST /api/recepciones/ordenes/{id}/liberaciones
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpPost("ordenes/{id:guid}/liberaciones")]
    [HttpPost("ordenes-compra/{id:guid}/liberaciones")]
    public async Task<IActionResult> LiberarOrdenCompra(
        [FromRoute] Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] LiberarOrdenCompraRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryObtenerUsuarioId(out Guid usuarioId))
        {
            return ProblemaUsuarioSinSubValido();
        }

        var resultado = await _liberarOrdenHandler.Handle(
            new LiberarOrdenCompraCommand(id, usuarioId, request?.Motivo),
            cancellationToken);

        return Ok(resultado);
    }

    /// <summary>
    /// Registra la liquidación administrativa de una orden de compra (#92).
    /// Bloquea la liquidación con 409 Conflict (ProblemDetails) mientras exista saldo pendiente de recepción (criterio CU-06).
    /// Roles permitidos: Comprador y Admin.
    /// POST /api/recepciones/ordenes/{id}/liquidaciones
    /// </summary>
    [Authorize(Roles = "Comprador,Admin")]
    [HttpPost("ordenes/{id:guid}/liquidaciones")]
    [HttpPost("ordenes-compra/{id:guid}/liquidaciones")]
    public async Task<IActionResult> LiquidarOrdenCompra(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!TryObtenerUsuarioId(out Guid usuarioId))
        {
            return ProblemaUsuarioSinSubValido();
        }

        var resultado = await _liquidarOrdenHandler.Handle(
            new LiquidarOrdenCompraCommand(id, usuarioId),
            cancellationToken);

        return Ok(resultado);
    }
}
