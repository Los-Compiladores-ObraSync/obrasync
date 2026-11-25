using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Exceptions;
using ObraSync.Recepcion.Application.Interfaces;

namespace ObraSync.Recepcion.Api.Controllers;

/// <summary>
/// Controlador base compartido para los controladores del microservicio de Recepción y Presupuesto (SRP / DRY).
/// Centraliza lectura de claims JWT, propagación de CorrelationId, cabeceras de paginación y validación de obras de Capataz.
/// </summary>
public abstract class RecepcionControllerBase : ControllerBase
{
    protected const string DetalleObraNoVerificablePorDefecto =
        "No se pudo verificar que la obra esté asignada al capataz porque el servicio Usuarios y Obras no respondió. Reintentá en unos segundos.";

    protected const string DetalleObrasAsignadasNoVerificables =
        "No se pudo verificar las obras asignadas al capataz porque el servicio Usuarios y Obras no respondió. Reintentá en unos segundos.";

    protected IObraAsignacionValidator? ObraAsignacionValidator { get; }

    protected RecepcionControllerBase(IObraAsignacionValidator? obraAsignacionValidator = null)
    {
        ObraAsignacionValidator = obraAsignacionValidator;
    }

    protected bool EsCapatazSinPrivilegiosSuperiores()
    {
        if (User?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        return User.IsInRole("Capataz") && !User.IsInRole("Admin") && !User.IsInRole("Comprador");
    }

    protected bool EsProveedorSinPrivilegiosSuperiores()
    {
        if (User?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        return User.IsInRole("Proveedor") && !User.IsInRole("Admin") && !User.IsInRole("Comprador");
    }

    protected bool TryObtenerUsuarioId(out Guid usuarioId)
    {
        string? usuarioIdTexto = User?.FindFirst("sub")?.Value
                                 ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(usuarioIdTexto, out usuarioId) && usuarioId != Guid.Empty;
    }

    protected ObjectResult ProblemaUsuarioSinSubValido() =>
        Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "No autorizado",
            detail: "El token JWT no contiene un claim 'sub' válido con formato GUID.");

    protected Guid GetCorrelationIdFromHeader()
    {
        var header = Request?.Headers["X-Correlation-ID"].ToString();
        return Guid.TryParse(header, out var correlationId) && correlationId != Guid.Empty
            ? correlationId
            : Guid.NewGuid();
    }

    protected string? GetAuthorizationHeader() => Request?.Headers["Authorization"].ToString();

    protected bool TryValidarPaginacion(
        int pagina,
        int tamano,
        out ParametrosPagina parametros,
        out ObjectResult? errorResult)
    {
        parametros = new ParametrosPagina(pagina, tamano);
        var error = parametros.Validar();
        if (error is not null)
        {
            errorResult = Problem(
                detail: error,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Paginación inválida");
            return false;
        }

        errorResult = null;
        return true;
    }

    protected void SetPaginationHeaders(int total, ParametrosPagina p)
    {
        if (Response?.Headers is not null)
        {
            Response.Headers["X-Total-Count"] = total.ToString();
            Response.Headers["X-Page"] = p.Pagina.ToString();
            Response.Headers["X-Page-Size"] = p.Tamano.ToString();
        }
    }

    protected async Task<ObjectResult?> ValidarObraAsignadaACapatazAsync(
        Guid obraId,
        Guid correlationId,
        CancellationToken cancellationToken,
        string detalleServicioNoDisponible = DetalleObraNoVerificablePorDefecto)
    {
        if (!EsCapatazSinPrivilegiosSuperiores() || ObraAsignacionValidator is null)
        {
            return null;
        }

        bool asignada;
        try
        {
            asignada = await ObraAsignacionValidator.EstaAsignadaACapatazAsync(
                obraId,
                GetAuthorizationHeader(),
                correlationId.ToString(),
                cancellationToken);
        }
        catch (ObraAsignacionNoVerificableException)
        {
            return Problem(
                detail: detalleServicioNoDisponible,
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Servicio no disponible");
        }

        if (!asignada)
        {
            return Problem(
                detail: "La obra no está asignada al capataz autenticado.",
                statusCode: StatusCodes.Status403Forbidden,
                title: "Obra no asignada");
        }

        return null;
    }

    protected async Task<(IReadOnlyList<Guid>? ObrasAsignadas, ObjectResult? ErrorResult)> ResolverFiltroObrasCapatazAsync(
        Guid? obraId,
        Guid correlationId,
        CancellationToken cancellationToken,
        string detalleObraNoVerificable = DetalleObraNoVerificablePorDefecto)
    {
        if (!EsCapatazSinPrivilegiosSuperiores() || ObraAsignacionValidator is null)
        {
            return (null, null);
        }

        if (obraId.HasValue)
        {
            var error = await ValidarObraAsignadaACapatazAsync(
                obraId.Value,
                correlationId,
                cancellationToken,
                detalleObraNoVerificable);

            return (null, error);
        }

        try
        {
            var obrasAsignadas = await ObraAsignacionValidator.ObtenerObrasAsignadasAsync(
                GetAuthorizationHeader(),
                correlationId.ToString(),
                cancellationToken);

            return (obrasAsignadas, null);
        }
        catch (ObraAsignacionNoVerificableException)
        {
            return (
                null,
                Problem(
                    detail: DetalleObrasAsignadasNoVerificables,
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Servicio no disponible"));
        }
    }
}
