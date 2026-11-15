using System.Text.Json;
using System.Text.Json.Nodes;

namespace ObraSync.Asistente.Application;

/// <summary>Resultado de una herramienta: ok + datos, o ok=false + código de error estable.</summary>
public sealed record ResultadoHerramienta(bool Ok, JsonNode Datos)
{
    public static ResultadoHerramienta Bien(JsonNode datos) => new(true, datos);
    public static ResultadoHerramienta Error(string codigo) =>
        new(false, new JsonObject { ["ok"] = false, ["error"] = codigo });
}

/// <summary>
/// Herramienta de SOLO LECTURA: busca insumos en el catálogo (real o falso, según el buscador).
/// Valida los argumentos ANTES de llamar a la API y devuelve errores estables.
/// </summary>
public sealed class HerramientasCatalogo
{
    public const int MaxTextoBusqueda = 60;

    private readonly IBuscadorInsumos _buscador;

    public HerramientasCatalogo(IBuscadorInsumos buscador) => _buscador = buscador;

    /// <summary>Descripción de las herramientas que se le muestran al modelo.</summary>
    public static JsonArray Definiciones() => new()
    {
        new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = "buscar_insumos",
                ["description"] = "Busca insumos de construcción en el catálogo por texto.",
                ["parameters"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["texto"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Palabra a buscar, por ejemplo: cemento"
                        }
                    },
                    ["required"] = new JsonArray("texto")
                }
            }
        }
    };

    /// <summary>Ejecuta una herramienta pedida por el modelo.</summary>
    public async Task<ResultadoHerramienta> EjecutarAsync(string nombre, string argumentosJson, CancellationToken ct)
    {
        if (nombre != "buscar_insumos")
            return ResultadoHerramienta.Error("herramienta_desconocida");

        string? texto;
        try
        {
            texto = JsonNode.Parse(argumentosJson)?["texto"]?.GetValue<string>();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return ResultadoHerramienta.Error("argumentos_invalidos");
        }

        if (string.IsNullOrWhiteSpace(texto) || texto.Length > MaxTextoBusqueda)
            return ResultadoHerramienta.Error("argumentos_invalidos");

        var resultado = await _buscador.BuscarAsync(texto, ct);
        if (resultado.Error is not null)
            return ResultadoHerramienta.Error(resultado.Error);

        var lista = new JsonArray();
        foreach (var insumo in resultado.Insumos!)
            lista.Add(new JsonObject
            {
                ["articuloId"] = insumo.ArticuloId,
                ["codigo"] = insumo.Codigo,
                ["descripcion"] = insumo.Descripcion,
                ["unidadMedida"] = insumo.UnidadMedida
            });
        return ResultadoHerramienta.Bien(new JsonObject { ["ok"] = true, ["insumos"] = lista });
    }
}
