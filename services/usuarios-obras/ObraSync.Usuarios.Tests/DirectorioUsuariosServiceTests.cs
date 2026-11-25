using NSubstitute;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Application.Interfaces;
using ObraSync.Usuarios.Application.Services;
using ObraSync.Usuarios.Domain.Entities;
using Xunit;

namespace ObraSync.Usuarios.Tests;

public class DirectorioUsuariosServiceTests
{
    private readonly IUsuarioQueryRepository _usuarioQueryRepository;
    private readonly DirectorioUsuariosService _service;

    public DirectorioUsuariosServiceTests()
    {
        _usuarioQueryRepository = Substitute.For<IUsuarioQueryRepository>();
        _service = new DirectorioUsuariosService(_usuarioQueryRepository);
    }

    [Fact]
    public async Task GetCapatacesActivosAsync_DebeDelegarEnRepositorioYRetornarCapataces()
    {
        var capatazId = Guid.NewGuid();
        IReadOnlyList<UsuarioResumenDto> resumen = new List<UsuarioResumenDto>
        {
            new(capatazId, "Carlos Capataz")
        };

        _usuarioQueryRepository
            .GetActivosPorRolPaginadosAsync(UserRoles.Capataz, new ParametrosPagina(), Arg.Any<CancellationToken>())
            .Returns((resumen, 1));

        var result = await _service.GetCapatacesActivosAsync();

        var unico = Assert.Single(result);
        Assert.Equal(capatazId, unico.Id);
        Assert.Equal("Carlos Capataz", unico.NombreCompleto);
    }

    [Fact]
    public async Task GetCapatacesActivosPaginadosAsync_DebeDelegarPaginacionEnBaseDeDatosSinCargarEnMemoria()
    {
        var idMenor = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var idMayor = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var idZeta = Guid.Parse("55555555-5555-5555-5555-555555555555");

        IReadOnlyList<UsuarioResumenDto> pagina1Repo = new List<UsuarioResumenDto>
        {
            new(idMenor, "Carlos Empate"),
            new(idMayor, "Carlos Empate")
        };
        IReadOnlyList<UsuarioResumenDto> pagina2Repo = new List<UsuarioResumenDto>
        {
            new(idZeta, "Zulema Capataz")
        };

        var p1 = new ParametrosPagina(1, 2);
        var p2 = new ParametrosPagina(2, 2);

        _usuarioQueryRepository
            .GetActivosPorRolPaginadosAsync(UserRoles.Capataz, p1, Arg.Any<CancellationToken>())
            .Returns((pagina1Repo, 3));
        _usuarioQueryRepository
            .GetActivosPorRolPaginadosAsync(UserRoles.Capataz, p2, Arg.Any<CancellationToken>())
            .Returns((pagina2Repo, 3));

        var (pag1, total1) = await _service.GetCapatacesActivosPaginadosAsync(p1);
        var (pag2, total2) = await _service.GetCapatacesActivosPaginadosAsync(p2);

        Assert.Equal(3, total1);
        Assert.Equal(3, total2);
        Assert.Equal(2, pag1.Count);
        Assert.Single(pag2);
        Assert.Equal(idMenor, pag1[0].Id);
        Assert.Equal(idMayor, pag1[1].Id);
        Assert.Equal(idZeta, pag2[0].Id);
    }

    [Fact]
    public async Task GetProveedoresActivosPaginadosAsync_DebeDelegarPaginacionYMantenerTelefono()
    {
        var idMenor = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var idMayor = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var idZeta = Guid.Parse("55555555-5555-5555-5555-555555555555");

        IReadOnlyList<UsuarioResumenDto> pagina1Repo = new List<UsuarioResumenDto>
        {
            new(idMenor, "Corralón Empate", "+54 9 11 1111-2222"),
            new(idMayor, "Corralón Empate")
        };
        IReadOnlyList<UsuarioResumenDto> pagina2Repo = new List<UsuarioResumenDto>
        {
            new(idZeta, "Zulema Materiales", "+54 9 11 3333-4444")
        };

        var p1 = new ParametrosPagina(1, 2);
        var p2 = new ParametrosPagina(2, 2);

        _usuarioQueryRepository
            .GetActivosPorRolPaginadosAsync(UserRoles.Proveedor, p1, Arg.Any<CancellationToken>())
            .Returns((pagina1Repo, 3));
        _usuarioQueryRepository
            .GetActivosPorRolPaginadosAsync(UserRoles.Proveedor, p2, Arg.Any<CancellationToken>())
            .Returns((pagina2Repo, 3));

        var (pag1, total1) = await _service.GetProveedoresActivosPaginadosAsync(p1);
        var (pag2, total2) = await _service.GetProveedoresActivosPaginadosAsync(p2);

        Assert.Equal(3, total1);
        Assert.Equal(3, total2);
        Assert.Equal(2, pag1.Count);
        Assert.Single(pag2);
        Assert.Equal(idMenor, pag1[0].Id);
        Assert.Equal("+54 9 11 1111-2222", pag1[0].Telefono);
        Assert.Equal(idMayor, pag1[1].Id);
        Assert.Null(pag1[1].Telefono);
        Assert.Equal(idZeta, pag2[0].Id);
    }

    [Fact]
    public async Task GetProveedoresActivosPaginadosAsync_SinParametros_UsaPaginacionPorDefecto()
    {
        IReadOnlyList<UsuarioResumenDto> vacio = Array.Empty<UsuarioResumenDto>();
        _usuarioQueryRepository
            .GetActivosPorRolPaginadosAsync(UserRoles.Proveedor, new ParametrosPagina(), Arg.Any<CancellationToken>())
            .Returns((vacio, 0));

        var (items, total) = await _service.GetProveedoresActivosPaginadosAsync();

        Assert.Equal(0, total);
        Assert.Empty(items);
    }
}
