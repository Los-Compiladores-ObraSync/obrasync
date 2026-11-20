namespace ObraSync.Asistente.Application.Generadas;

/// <summary>Carga los contratos OpenAPI de una carpeta (los *-openapi.json de contracts/openapi) y genera las herramientas.</summary>
public static class CatalogoHerramientasGeneradas
{
    public static IReadOnlyList<HerramientaGenerada> CargarDeCarpeta(string carpeta)
    {
        if (!Directory.Exists(carpeta))
            return Array.Empty<HerramientaGenerada>();

        var contratos = Directory.EnumerateFiles(carpeta, "*-openapi.json")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(File.ReadAllText);
        return GeneradorHerramientasOpenApi.Generar(contratos);
    }
}
