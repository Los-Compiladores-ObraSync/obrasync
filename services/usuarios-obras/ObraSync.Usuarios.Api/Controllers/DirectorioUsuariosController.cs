using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Application.Interfaces;
using ObraSync.Usuarios.Domain.Entities;

namespace ObraSync.Usuarios.Api.Controllers;

[ApiController]
[Route("api/usuarios")]
public class DirectorioUsuariosController : ControllerBase
{
    private readonly IDirectorioUsuariosService _directorioUsuariosService;

    public DirectorioUsuariosController(IDirectorioUsuariosService directorioUsuariosService)
    {
        _directorioUsuariosService = directorioUsuariosService ?? throw new ArgumentNullException(nameof(directorioUsuariosService));
    }

    [HttpGet("capataces")]
    [Authorize(Roles = $"{UserRoles.Comprador},{UserRoles.Admin}")]
    [ProducesResponseType(typeof(IEnumerable<CapatazDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IEnumerable<CapatazDto>>> GetCapatacesActivos(
        [FromQuery] int pagina = 1,
        [FromQuery] int tamano = ParametrosPagina.TamanoMaximo)
    {
        var p = new ParametrosPagina(pagina, tamano);
        var error = p.Validar();
        if (error is not null)
        {
            return Problem(detail: error, statusCode: StatusCodes.Status400BadRequest, title: "Paginación inválida");
        }

        var (capataces, total) = await _directorioUsuariosService.GetCapatacesActivosPaginadosAsync(p);

        if (Response?.Headers is not null)
        {
            Response.Headers["X-Total-Count"] = total.ToString();
            Response.Headers["X-Page"] = p.Pagina.ToString();
            Response.Headers["X-Page-Size"] = p.Tamano.ToString();
        }

        return Ok(capataces);
    }

    /// <summary>
    /// Listar proveedores activos (Roles: Comprador, Admin). Compras lo usa para elegir a quién generarle un Magic Link (#157).
    /// </summary>
    [HttpGet("proveedores")]
    [Authorize(Roles = $"{UserRoles.Comprador},{UserRoles.Admin}")]
    [ProducesResponseType(typeof(IEnumerable<ProveedorDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IEnumerable<ProveedorDto>>> GetProveedoresActivos(
        [FromQuery] int pagina = 1,
        [FromQuery] int tamano = ParametrosPagina.TamanoMaximo)
    {
        var p = new ParametrosPagina(pagina, tamano);
        var error = p.Validar();
        if (error is not null)
        {
            return Problem(detail: error, statusCode: StatusCodes.Status400BadRequest, title: "Paginación inválida");
        }

        var (proveedores, total) = await _directorioUsuariosService.GetProveedoresActivosPaginadosAsync(p);

        if (Response?.Headers is not null)
        {
            Response.Headers["X-Total-Count"] = total.ToString();
            Response.Headers["X-Page"] = p.Pagina.ToString();
            Response.Headers["X-Page-Size"] = p.Tamano.ToString();
        }

        return Ok(proveedores);
    }
}
