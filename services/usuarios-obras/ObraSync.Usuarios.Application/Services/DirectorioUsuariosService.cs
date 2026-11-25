using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Application.Interfaces;
using ObraSync.Usuarios.Domain.Entities;

namespace ObraSync.Usuarios.Application.Services;

public class DirectorioUsuariosService : IDirectorioUsuariosService
{
    private readonly IUsuarioQueryRepository _usuarioQueryRepository;

    public DirectorioUsuariosService(IUsuarioQueryRepository usuarioQueryRepository)
    {
        _usuarioQueryRepository = usuarioQueryRepository ?? throw new ArgumentNullException(nameof(usuarioQueryRepository));
    }

    public async Task<IEnumerable<CapatazDto>> GetCapatacesActivosAsync()
    {
        var (items, _) = await GetCapatacesActivosPaginadosAsync(new ParametrosPagina());
        return items;
    }

    public Task<(IReadOnlyList<CapatazDto> Items, int Total)> GetCapatacesActivosPaginadosAsync(
        ParametrosPagina? paginacion = null)
        => GetActivosPorRolPaginadosAsync(
            UserRoles.Capataz,
            paginacion,
            u => new CapatazDto(u.Id, u.NombreCompleto));

    public Task<(IReadOnlyList<ProveedorDto> Items, int Total)> GetProveedoresActivosPaginadosAsync(
        ParametrosPagina? paginacion = null)
        => GetActivosPorRolPaginadosAsync(
            UserRoles.Proveedor,
            paginacion,
            u => new ProveedorDto(u.Id, u.NombreCompleto, u.Telefono));

    private async Task<(IReadOnlyList<TDto> Items, int Total)> GetActivosPorRolPaginadosAsync<TDto>(
        string rol,
        ParametrosPagina? paginacion,
        Func<UsuarioResumenDto, TDto> mapper)
    {
        var p = paginacion ?? new ParametrosPagina();
        var (usuarios, total) = await _usuarioQueryRepository.GetActivosPorRolPaginadosAsync(rol, p);
        var items = usuarios.Select(mapper).ToList();
        return (items, total);
    }
}
