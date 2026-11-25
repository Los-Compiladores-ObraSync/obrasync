using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Licitaciones.Api.Hubs;
using ObraSync.Licitaciones.Application.DTOs;

namespace ObraSync.Licitaciones.Api.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected bool TryObtenerUsuarioId(out Guid usuarioId) =>
        JwtUserClaimsHelper.TryGetUsuarioId(User, out usuarioId);

    protected string? ObtenerRolUsuario() =>
        JwtUserClaimsHelper.ObtenerRolUsuario(User);

    protected string? ObtenerRolConsulta() =>
        User.IsInRole("Proveedor")
            ? "Proveedor"
            : User.FindFirstValue("role") ?? User.FindFirstValue(ClaimTypes.Role);

    protected string? ObtenerNombreUsuario() =>
        JwtUserClaimsHelper.ObtenerNombreUsuario(User);

    protected Guid ObtenerCorrelationId()
    {
        var correlationHeader = HttpContext?.Request.Headers["X-Correlation-ID"].FirstOrDefault()
            ?? HttpContext?.Items["CorrelationId"]?.ToString();

        return Guid.TryParse(correlationHeader, out var parsed) && parsed != Guid.Empty
            ? parsed
            : Guid.NewGuid();
    }

    protected bool TryCrearPaginacion(
        int pagina,
        int tamano,
        out ParametrosPagina paginacion,
        out ObjectResult? errorResult)
    {
        paginacion = new ParametrosPagina(pagina, tamano);
        var error = paginacion.Validar();
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

    protected void EscribirHeadersPaginacion(int total, ParametrosPagina paginacion)
    {
        Response.Headers["X-Total-Count"] = total.ToString();
        Response.Headers["X-Page"] = paginacion.Pagina.ToString();
        Response.Headers["X-Page-Size"] = paginacion.Tamano.ToString();
    }

    protected ObjectResult UsuarioNoIdentificadoProblem(
        string detail = "El token JWT no contiene un claim 'sub' válido con formato GUID para identificar al usuario.",
        string title = "Usuario no identificado") =>
        Problem(
            detail: detail,
            statusCode: StatusCodes.Status401Unauthorized,
            title: title);
}
