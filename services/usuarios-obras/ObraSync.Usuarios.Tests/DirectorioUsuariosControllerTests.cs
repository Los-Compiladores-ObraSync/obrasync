using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using ObraSync.Usuarios.Api.Controllers;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Application.Interfaces;
using Xunit;

namespace ObraSync.Usuarios.Tests;

public class DirectorioUsuariosControllerTests
{
    private readonly IDirectorioUsuariosService _directorioService;
    private readonly DirectorioUsuariosController _controller;

    public DirectorioUsuariosControllerTests()
    {
        _directorioService = Substitute.For<IDirectorioUsuariosService>();
        _controller = new DirectorioUsuariosController(_directorioService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    [Fact]
    public async Task GetCapatacesActivos_PaginacionValida_Retorna200YHeaders()
    {
        var items = new List<CapatazDto>
        {
            new(Guid.NewGuid(), "Ana Capataz"),
            new(Guid.NewGuid(), "Carlos Capataz")
        };
        _directorioService.GetCapatacesActivosPaginadosAsync(new ParametrosPagina(1, 2))
            .Returns((items, 5));

        var result = await _controller.GetCapatacesActivos(pagina: 1, tamano: 2);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(items, ok.Value);
        Assert.Equal("5", _controller.Response.Headers["X-Total-Count"].ToString());
        Assert.Equal("1", _controller.Response.Headers["X-Page"].ToString());
        Assert.Equal("2", _controller.Response.Headers["X-Page-Size"].ToString());
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 51)]
    public async Task GetCapatacesActivos_ParametrosInvalidos_Retorna400ProblemDetails(int pagina, int tamano)
    {
        var result = await _controller.GetCapatacesActivos(pagina, tamano);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal("Paginación inválida", problem.Title);
    }

    [Fact]
    public async Task GetProveedoresActivos_PaginacionValida_Retorna200YHeaders()
    {
        var items = new List<ProveedorDto>
        {
            new(Guid.NewGuid(), "Corralón Norte"),
            new(Guid.NewGuid(), "Hierros del Sur")
        };
        _directorioService.GetProveedoresActivosPaginadosAsync(new ParametrosPagina(1, 2))
            .Returns((items, 5));

        var result = await _controller.GetProveedoresActivos(pagina: 1, tamano: 2);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(items, ok.Value);
        Assert.Equal("5", _controller.Response.Headers["X-Total-Count"].ToString());
        Assert.Equal("1", _controller.Response.Headers["X-Page"].ToString());
        Assert.Equal("2", _controller.Response.Headers["X-Page-Size"].ToString());
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 51)]
    public async Task GetProveedoresActivos_ParametrosInvalidos_Retorna400ProblemDetails(int pagina, int tamano)
    {
        var result = await _controller.GetProveedoresActivos(pagina, tamano);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal("Paginación inválida", problem.Title);
    }
}
