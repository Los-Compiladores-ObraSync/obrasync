using System.Text.Json.Nodes;

namespace ObraSync.Asistente.Application;

/// <summary>
/// Contrato polimórfico de un grupo de herramientas del asistente (OCP y DIP).
/// Permite agregar nuevos módulos de herramientas registrando una nueva implementación en el contenedor,
/// sin modificar <see cref="Agente"/> ni acoplarlo a clases concretas.
/// </summary>
public interface IGrupoHerramientas
{
    /// <summary>Indica si este grupo define la herramienta con ese nombre.</summary>
    bool Contiene(string nombre);

    /// <summary>Indica si el perfil indicado tiene permiso para ver y ejecutar esa herramienta.</summary>
    bool PermiteRol(PerfilAsistente perfil, string nombre);

    /// <summary>Definiciones JSON (formato function-calling) que este grupo ofrece al perfil indicado.</summary>
    IEnumerable<JsonNode> DefinicionesPara(PerfilAsistente perfil);

    /// <summary>
    /// Ejecuta una herramienta del grupo validando el perfil del usuario.
    /// Si el perfil no tiene permiso, devuelve el error estable "herramienta_no_permitida".
    /// </summary>
    Task<ResultadoHerramienta> EjecutarAsync(
        PerfilAsistente perfil,
        string nombre,
        string argumentosJson,
        CancellationToken ct);
}
