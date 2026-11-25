namespace ObraSync.Asistente.Application;

/// <summary>
/// Puerto de ejecución de las herramientas de catálogo y borrador de requisición (ISP y DIP),
/// consumido por el adaptador MCP sin acoplarse a la clase concreta <see cref="HerramientasCatalogo"/>.
/// </summary>
public interface IHerramientasCatalogo
{
    Task<ResultadoHerramienta> EjecutarAsync(string nombre, string argumentosJson, CancellationToken ct);
}
