using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Queries;

namespace ObraSync.Recepcion.Api.Controllers;

/// <summary>
/// Controlador enfocado en la descarga segura de fotos de evidencia asociadas a recepciones con problema (SRP / DIP).
/// Mantiene exactamente la ruta <c>GET api/recepciones/{id}/fotos/{fotoId}</c>.
/// </summary>
[Authorize]
[ApiController]
[Route("api/recepciones")]
public class FotosRecepcionController : RecepcionControllerBase
{
    private readonly IQueryHandler<ObtenerFotoRecepcionQuery, FotoDescargaDto?> _obtenerFotoHandler;

    public FotosRecepcionController(
        IQueryHandler<ObtenerFotoRecepcionQuery, FotoDescargaDto?> obtenerFotoHandler,
        IObraAsignacionValidator? obraAsignacionValidator = null)
        : base(obraAsignacionValidator)
    {
        _obtenerFotoHandler = obtenerFotoHandler ?? throw new ArgumentNullException(nameof(obtenerFotoHandler));
    }

    /// <summary>
    /// Descarga una foto de evidencia asociada a una entrega con problema (#217), con Cache-Control privado.
    /// Roles permitidos: Capataz (obra asignada), Comprador, Admin y Proveedor (solo dueño de la orden).
    /// </summary>
    [Authorize(Roles = "Capataz,Comprador,Admin,Proveedor")]
    [HttpGet("{id:guid}/fotos/{fotoId:guid}")]
    public async Task<IActionResult> DescargarFotoRecepcion(
        [FromRoute] Guid id,
        [FromRoute] Guid fotoId,
        CancellationToken cancellationToken = default)
    {
        var foto = await _obtenerFotoHandler.Handle(
            new ObtenerFotoRecepcionQuery(id, fotoId),
            cancellationToken);

        if (foto is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                detail: "La foto de evidencia no existe.");
        }

        if (EsCapatazSinPrivilegiosSuperiores())
        {
            var errorObra = await ValidarObraAsignadaACapatazAsync(
                foto.ObraId,
                GetCorrelationIdFromHeader(),
                cancellationToken);

            if (errorObra is not null)
            {
                return errorObra;
            }
        }
        else if (EsProveedorSinPrivilegiosSuperiores())
        {
            if (!TryObtenerUsuarioId(out var proveedorUsuarioId))
            {
                return Unauthorized();
            }

            if (!foto.ProveedorId.HasValue || foto.ProveedorId.Value != proveedorUsuarioId)
            {
                return Problem(
                    detail: "No tenés permisos para descargar fotos de esta orden de compra.",
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Acceso denegado");
            }
        }

        if (Response?.Headers is not null)
        {
            Response.Headers["Cache-Control"] = "private, max-age=3600";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
        }

        return File(foto.Contenido, foto.ContentType);
    }
}
