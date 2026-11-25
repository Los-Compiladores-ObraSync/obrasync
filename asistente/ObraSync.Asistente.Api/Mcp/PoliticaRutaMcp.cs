using Microsoft.AspNetCore.Http;

namespace ObraSync.Asistente.Api.Mcp;

/// <summary>
/// Define la política de aislamiento de una ruta MCP: su camino HTTP, los roles autorizados
/// y la lista blanca cerrada de herramientas que esa ruta puede listar y ejecutar.
/// Permite agregar nuevas rutas MCP registrando nuevas políticas sin modificar el filtro (OCP).
/// </summary>
public sealed class PoliticaRutaMcp
{
    private readonly HashSet<string> _herramientasPermitidas;
    private readonly bool _coincidenciaExacta;

    public string Ruta { get; }
    public string RolesAutorizados { get; }
    public IReadOnlyCollection<string> HerramientasPermitidas => _herramientasPermitidas;

    public PoliticaRutaMcp(
        string ruta,
        string rolesAutorizados,
        IEnumerable<string> herramientasPermitidas,
        bool coincidenciaExacta = false)
    {
        Ruta = ruta;
        RolesAutorizados = rolesAutorizados;
        _coincidenciaExacta = coincidenciaExacta;
        _herramientasPermitidas = new HashSet<string>(herramientasPermitidas, StringComparer.Ordinal);
    }

    public bool CoincideRuta(PathString path) =>
        _coincidenciaExacta
            ? path.Equals(Ruta, StringComparison.OrdinalIgnoreCase)
            : path.StartsWithSegments(Ruta, StringComparison.OrdinalIgnoreCase);

    public bool PermiteHerramienta(string? nombre) =>
        nombre is not null && _herramientasPermitidas.Contains(nombre);
}
