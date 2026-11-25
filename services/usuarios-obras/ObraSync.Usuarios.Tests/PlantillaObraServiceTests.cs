using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using ObraSync.Usuarios.Api.Controllers;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Application.Interfaces;
using ObraSync.Usuarios.Application.Services;
using ObraSync.Usuarios.Domain.Entities;
using ObraSync.Usuarios.Domain.Exceptions;
using Xunit;

namespace ObraSync.Usuarios.Tests;

public class PlantillaObraServiceTests
{
    private readonly IPlantillaObraRepository _repository;
    private readonly PlantillaObraService _service;

    public PlantillaObraServiceTests()
    {
        _repository = Substitute.For<IPlantillaObraRepository>();
        _service = new PlantillaObraService(_repository);
    }

    [Fact]
    public async Task ObtenerPaginadasAsync_DebeDelegarEnRepositorioYOrdenarEtapasPorOrden()
    {
        var plantillaId = Guid.NewGuid();
        var etapa1Id = Guid.NewGuid();
        var etapa2Id = Guid.NewGuid();

        IReadOnlyList<PlantillaObra> entidades = new List<PlantillaObra>
        {
            new()
            {
                Id = plantillaId,
                Nombre = "Vivienda Unifamiliar",
                Descripcion = "Casa de dos plantas",
                Etapas = new List<EtapaPlantilla>
                {
                    new() { Id = etapa2Id, NombreEtapa = "Techado", Orden = 2 },
                    new() { Id = etapa1Id, NombreEtapa = "Cimientos", Orden = 1 }
                }
            }
        };

        var paginacion = new ParametrosPagina(1, 10);
        _repository.ObtenerPaginadasAsync(paginacion, Arg.Any<CancellationToken>())
            .Returns((entidades, 1));

        var (items, total) = await _service.ObtenerPaginadasAsync(paginacion);

        Assert.Equal(1, total);
        var dto = Assert.Single(items);
        Assert.Equal(plantillaId, dto.Id);
        Assert.Equal("Vivienda Unifamiliar", dto.Nombre);
        Assert.Equal(2, dto.Etapas.Count);
        Assert.Equal("Cimientos", dto.Etapas[0].NombreEtapa);
        Assert.Equal(1, dto.Etapas[0].Orden);
        Assert.Equal("Techado", dto.Etapas[1].NombreEtapa);
        Assert.Equal(2, dto.Etapas[1].Orden);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 51)]
    public async Task ObtenerPaginadasAsync_ParametrosInvalidos_DebeLanzarValidacionException(int pagina, int tamano)
    {
        await Assert.ThrowsAsync<ValidacionException>(() =>
            _service.ObtenerPaginadasAsync(new ParametrosPagina(pagina, tamano)));

        await _repository.DidNotReceiveWithAnyArgs()
            .ObtenerPaginadasAsync(default!, default);
    }

    [Fact]
    public async Task ObtenerPorIdAsync_CuandoExiste_RetornaDtoConEtapasOrdenadas()
    {
        var id = Guid.NewGuid();
        var entidad = new PlantillaObra
        {
            Id = id,
            Nombre = "Nave Industrial",
            Descripcion = "Galpón",
            Etapas = new List<EtapaPlantilla>
            {
                new() { Id = Guid.NewGuid(), NombreEtapa = "Estructura", Orden = 2 },
                new() { Id = Guid.NewGuid(), NombreEtapa = "Suelos", Orden = 1 }
            }
        };

        _repository.ObtenerPorIdAsync(id, Arg.Any<CancellationToken>())
            .Returns(entidad);

        var resultado = await _service.ObtenerPorIdAsync(id);

        Assert.NotNull(resultado);
        Assert.Equal(id, resultado.Id);
        Assert.Equal("Suelos", resultado.Etapas[0].NombreEtapa);
        Assert.Equal("Estructura", resultado.Etapas[1].NombreEtapa);
    }

    [Fact]
    public async Task ObtenerPorIdAsync_CuandoNoExiste_RetornaNull()
    {
        var id = Guid.NewGuid();
        _repository.ObtenerPorIdAsync(id, Arg.Any<CancellationToken>())
            .Returns((PlantillaObra?)null);

        var resultado = await _service.ObtenerPorIdAsync(id);

        Assert.Null(resultado);
    }

    [Fact]
    public async Task CrearAsync_ConEtapas_AsignaOrdenSecuencialYPersisteEnRepositorio()
    {
        var request = new CrearPlantillaObraRequestDto
        {
            Nombre = "Dúplex",
            Descripcion = "Prototipo estándar",
            Etapas = new List<string> { "Replanteo", "Fundaciones", "Mampostería" }
        };

        _repository.CrearAsync(Arg.Any<PlantillaObra>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<PlantillaObra>());

        var resultado = await _service.CrearAsync(request);

        Assert.NotEqual(Guid.Empty, resultado.Id);
        Assert.Equal("Dúplex", resultado.Nombre);
        Assert.Equal("Prototipo estándar", resultado.Descripcion);
        Assert.Equal(3, resultado.Etapas.Count);
        Assert.Equal((1, "Replanteo"), (resultado.Etapas[0].Orden, resultado.Etapas[0].NombreEtapa));
        Assert.Equal((2, "Fundaciones"), (resultado.Etapas[1].Orden, resultado.Etapas[1].NombreEtapa));
        Assert.Equal((3, "Mampostería"), (resultado.Etapas[2].Orden, resultado.Etapas[2].NombreEtapa));
        await _repository.Received(1).CrearAsync(Arg.Any<PlantillaObra>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CrearAsync_SinNombre_DebeLanzarValidacionException()
    {
        var request = new CrearPlantillaObraRequestDto { Nombre = "   " };

        await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(request));
        await _repository.DidNotReceiveWithAnyArgs().CrearAsync(default!, default);
    }

    [Fact]
    public async Task PlantillasObraController_ObtenerPorIdInexistente_Retorna404ConMensajeExacto()
    {
        var serviceMock = Substitute.For<IPlantillaObraService>();
        var controller = new PlantillasObraController(serviceMock)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var id = Guid.NewGuid();
        serviceMock.ObtenerPorIdAsync(id, Arg.Any<CancellationToken>())
            .Returns((PlantillaObraResponseDto?)null);

        var result = await controller.ObtenerPorId(id);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(notFound.Value);
        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
        Assert.Equal("Plantilla no encontrada", problem.Title);
        Assert.Equal($"No se encontró la plantilla con id {id}", problem.Detail);
    }
}
