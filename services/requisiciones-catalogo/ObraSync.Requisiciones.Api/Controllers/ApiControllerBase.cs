using System;
using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Requisiciones.Api.Exceptions;
using ObraSync.Requisiciones.Api.Middlewares;
using ObraSync.Requisiciones.Application.DTOs;

namespace ObraSync.Requisiciones.Api.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected Guid ObtenerUsuarioId()
    {
        var subClaim = User.FindFirstValue("sub")
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(subClaim, out var userId) || userId == Guid.Empty)
        {
            throw new UsuarioNoIdentificadoException();
        }

        return userId;
    }

    protected bool EsCapatazSinPrivilegiosSuperiores() =>
        User.IsInRole("Capataz") && !User.IsInRole("Comprador") && !User.IsInRole("Admin");

    protected Guid? ObtenerSolicitanteRestringidoId() =>
        EsCapatazSinPrivilegiosSuperiores() ? ObtenerUsuarioId() : null;

    protected Guid ObtenerCorrelationId()
    {
        var header = HttpContext?.Items[CorrelationIdMiddleware.CorrelationIdItemKey] as string
            ?? Request.Headers[CorrelationIdMiddleware.CorrelationIdHeader].ToString();

        return Guid.TryParse(header, out var correlationId) && correlationId != Guid.Empty
            ? correlationId
            : Guid.NewGuid();
    }

    protected string? ObtenerAuthorizationHeader() =>
        Request?.Headers.Authorization.ToString();

    protected ActionResult<IReadOnlyList<T>> OkPaginado<T>(PaginaDe<T> resultado, ParametrosPagina paginacion)
    {
        Response.Headers["X-Total-Count"] = resultado.Total.ToString();
        Response.Headers["X-Page"] = paginacion.Pagina.ToString();
        Response.Headers["X-Page-Size"] = paginacion.Tamano.ToString();
        return Ok(resultado.Items);
    }
}
