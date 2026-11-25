using ObraSync.Usuarios.Application.DTOs;

namespace ObraSync.Usuarios.Application.Interfaces;

public interface IDirectorioUsuariosService
{
    Task<IEnumerable<CapatazDto>> GetCapatacesActivosAsync();
    Task<(IReadOnlyList<CapatazDto> Items, int Total)> GetCapatacesActivosPaginadosAsync(ParametrosPagina? paginacion = null);
    Task<(IReadOnlyList<ProveedorDto> Items, int Total)> GetProveedoresActivosPaginadosAsync(ParametrosPagina? paginacion = null);
}
