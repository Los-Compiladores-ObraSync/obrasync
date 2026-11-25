using System.Text.Json.Nodes;

namespace ObraSync.Asistente.Application;

/// <summary>
/// Base común para los grupos de herramientas manuales cuya visibilidad por rol está declarada en <see cref="PerfilAsistente"/>.
/// Centraliza el filtrado de definiciones y la defensa de permisos por perfil (SRP y LSP).
/// </summary>
public abstract class GrupoHerramientasDePerfil : IGrupoHerramientas
{
    protected abstract IReadOnlyList<string> HerramientasDelGrupo { get; }
    protected abstract JsonArray ObtenerDefiniciones();

    public bool Contiene(string nombre) =>
        HerramientasDelGrupo.Contains(nombre, StringComparer.Ordinal);

    public bool PermiteRol(PerfilAsistente perfil, string nombre) =>
        Contiene(nombre) && perfil.PermiteHerramienta(nombre);

    public IEnumerable<JsonNode> DefinicionesPara(PerfilAsistente perfil)
    {
        foreach (var definicion in ObtenerDefiniciones())
        {
            var nombre = definicion?["function"]?["name"]?.GetValue<string>();
            if (nombre is not null && PermiteRol(perfil, nombre))
                yield return definicion!.DeepClone();
        }
    }

    public Task<ResultadoHerramienta> EjecutarAsync(
        PerfilAsistente perfil,
        string nombre,
        string argumentosJson,
        CancellationToken ct)
    {
        if (!Contiene(nombre))
            return Task.FromResult(ResultadoHerramienta.Error("herramienta_desconocida"));
        if (!PermiteRol(perfil, nombre))
            return Task.FromResult(ResultadoHerramienta.Error("herramienta_no_permitida"));

        return EjecutarAsync(nombre, argumentosJson, ct);
    }

    /// <summary>Ejecuta una herramienta del grupo con los argumentos JSON crudos.</summary>
    public abstract Task<ResultadoHerramienta> EjecutarAsync(
        string nombre,
        string argumentosJson,
        CancellationToken ct);
}

/// <summary>
/// Objeto nulo para grupos opcionales no inyectados en el constructor de compatibilidad de <see cref="Agente"/>:
/// conserva las definiciones visibles del perfil y responde "herramienta_no_permitida" o "herramienta_no_disponible" según corresponda.
/// </summary>
internal sealed class GrupoHerramientasNoDisponible : GrupoHerramientasDePerfil
{
    private readonly IReadOnlyList<string> _nombres;
    private readonly Func<JsonArray> _definiciones;

    public GrupoHerramientasNoDisponible(IReadOnlyList<string> nombres, Func<JsonArray> definiciones)
    {
        _nombres = nombres;
        _definiciones = definiciones;
    }

    protected override IReadOnlyList<string> HerramientasDelGrupo => _nombres;
    protected override JsonArray ObtenerDefiniciones() => _definiciones();

    public override Task<ResultadoHerramienta> EjecutarAsync(
        string nombre,
        string argumentosJson,
        CancellationToken ct) =>
        Task.FromResult(ResultadoHerramienta.Error("herramienta_no_disponible"));
}
