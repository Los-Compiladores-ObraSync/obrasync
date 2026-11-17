using System.Text.Json;
using System.Text.Json.Nodes;

namespace ObraSync.Asistente.Application;

/// <summary>
/// Herramientas de licitaciones y cotizaciones del perfil Comprador (tarjeta #148). TODAS son de solo lectura:
///  - listar_licitaciones: las licitaciones más nuevas, con su estado, y cuántas hay en total.
///  - ver_licitacion: el detalle de una (materiales a cotizar, fecha límite).
///  - listar_cotizaciones: las ofertas de una licitación, con el puntaje y los costos que calcula el backend.
/// REGLA: el modelo NO hace cuentas ni calcula puntajes. Cada cifra (puntaje, costo total, flete, plazo) sale de lo que
/// devuelve la API y se le pasa tal cual; las instrucciones del perfil le piden copiarlas.
/// Igual que HerramientasCompras, validan los argumentos ANTES de llamar a la API y devuelven errores estables.
/// El texto que viene de la base (nombre del proveedor, descripciones) se pasa como dato, recortado y sin caracteres
/// de control: las instrucciones del agente le dicen al modelo que ignore cualquier orden que aparezca ahí.
/// </summary>
public sealed class HerramientasLicitaciones
{
    public const string ListarLicitaciones = "listar_licitaciones";
    public const string VerLicitacion = "ver_licitacion";
    public const string ListarCotizaciones = "listar_cotizaciones";

    /// <summary>El modelo no necesita ver 50 licitaciones ni 50 ofertas: se piden ya acotadas (cuida el cupo de tokens).</summary>
    public const int MaxLicitaciones = 5;
    public const int MaxCotizaciones = 10;
    public const int MaxTextoLibre = 200;

    /// <summary>Estados que maneja Licitaciones (compara sin distinguir mayúsculas).</summary>
    public static readonly IReadOnlyList<string> Estados = new[] { "Abierta", "Cerrada", "Adjudicada" };

    public static readonly IReadOnlyList<string> Nombres = new[] { ListarLicitaciones, VerLicitacion, ListarCotizaciones };

    private readonly IConsultorLicitaciones _licitaciones;
    private readonly IBuscadorObras _obras;

    public HerramientasLicitaciones(IConsultorLicitaciones licitaciones, IBuscadorObras obras)
    {
        _licitaciones = licitaciones;
        _obras = obras;
    }

    /// <summary>Descripción de las herramientas que se le muestran al modelo.</summary>
    public static JsonArray Definiciones() => new()
    {
        new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = ListarLicitaciones,
                ["description"] =
                    "Lista las licitaciones (pliegos para que los proveedores coticen) más nuevas, máximo 5, y dice cuántas hay en total. " +
                    "Se puede filtrar por estado. Cada una trae el numeroRequisicion del que salió, la obra y hasta cuándo se cotiza. " +
                    "Si hayMas es true, hay más licitaciones que las que devolvió: decile a la persona cuántas hay en total y que puede pedirte un estado en particular. Solo lectura.",
                ["parameters"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["estado"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JsonArray("Abierta", "Cerrada", "Adjudicada"),
                            ["description"] = "Opcional. Abierta es la que todavía recibe ofertas."
                        }
                    }
                }
            }
        },
        new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = VerLicitacion,
                ["description"] =
                    "Muestra el detalle de una licitación: estado, hasta cuándo se cotiza y los materiales con cantidades que se pidieron. " +
                    "Se pide con el licitacionId que devolvió listar_licitaciones. Solo lectura.",
                ["parameters"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["licitacionId"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Identificador (GUID) de la licitación, tal como lo devolvió listar_licitaciones."
                        }
                    },
                    ["required"] = new JsonArray("licitacionId")
                }
            }
        },
        new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = ListarCotizaciones,
                ["description"] =
                    "Lista las ofertas (cotizaciones) que recibió una licitación, hasta 10, con precio de los materiales, flete, costo total, plazo en días y el puntaje " +
                    "que calculó el sistema. Sirve para comparar ofertas. Las cifras las calcula el sistema: copialas tal cual, no hagas cuentas ni calcules puntajes. " +
                    "esGanadora es true en la de mayor puntaje (solo si hay 2 o más ofertas completas). Si hayMas es true no ves todas las ofertas: decilo y no afirmes cuál es la mejor. " +
                    "Se pide con el licitacionId que devolvió listar_licitaciones. Solo lectura.",
                ["parameters"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["licitacionId"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Identificador (GUID) de la licitación, tal como lo devolvió listar_licitaciones."
                        }
                    },
                    ["required"] = new JsonArray("licitacionId")
                }
            }
        }
    };

    /// <summary>Ejecuta una herramienta pedida por el modelo.</summary>
    public Task<ResultadoHerramienta> EjecutarAsync(string nombre, string argumentosJson, CancellationToken ct) =>
        nombre switch
        {
            ListarLicitaciones => ListarAsync(argumentosJson, ct),
            VerLicitacion => VerAsync(argumentosJson, ct),
            ListarCotizaciones => ListarCotizacionesAsync(argumentosJson, ct),
            _ => Task.FromResult(ResultadoHerramienta.Error("herramienta_desconocida"))
        };

    private async Task<ResultadoHerramienta> ListarAsync(string argumentosJson, CancellationToken ct)
    {
        if (!TryLeerArgumentos(argumentosJson, out var args))
            return ResultadoHerramienta.Error("argumentos_invalidos");

        string? estado = null;
        var nodoEstado = args["estado"];
        if (nodoEstado is not null)
        {
            // Un estado vacío o null significa "todas"; cualquier otra cosa tiene que ser un estado conocido.
            var texto = LeerTexto(nodoEstado);
            if (texto is null)
                return ResultadoHerramienta.Error("argumentos_invalidos");
            if (!string.IsNullOrWhiteSpace(texto))
            {
                estado = Estados.FirstOrDefault(e => string.Equals(e, texto.Trim(), StringComparison.OrdinalIgnoreCase));
                if (estado is null)
                    return ResultadoHerramienta.Error("argumentos_invalidos");
            }
        }

        var resultado = await _licitaciones.ListarAsync(estado, MaxLicitaciones, ct);
        if (resultado.Error is not null)
            return ResultadoHerramienta.Error(resultado.Error);

        var nombresDeObras = await NombresDeObrasAsync(ct);
        var lista = new JsonArray();
        foreach (var l in resultado.Licitaciones!.Take(MaxLicitaciones))
            lista.Add(ResumenJson(l, nombresDeObras));

        var total = resultado.Total ?? resultado.Licitaciones!.Count;
        var datos = new JsonObject
        {
            ["ok"] = true,
            ["total"] = total,
            // El modelo no tiene que adivinar si falta alguna: se lo decimos con un dato.
            ["mostradas"] = lista.Count,
            ["hayMas"] = total > lista.Count,
            ["licitaciones"] = lista
        };
        return ResultadoHerramienta.Bien(datos);
    }

    private async Task<ResultadoHerramienta> VerAsync(string argumentosJson, CancellationToken ct)
    {
        if (!TryLeerId(argumentosJson, out var id))
            return ResultadoHerramienta.Error("argumentos_invalidos");

        var resultado = await _licitaciones.ObtenerAsync(id, ct);
        if (resultado.Error is not null)
            return ResultadoHerramienta.Error(resultado.Error);

        var detalle = resultado.Licitacion!;
        var nombresDeObras = await NombresDeObrasAsync(ct);

        var items = new JsonArray();
        foreach (var i in detalle.Items)
            items.Add(new JsonObject
            {
                ["descripcion"] = Acotar(i.Descripcion),
                ["cantidad"] = i.Cantidad,
                ["unidadMedida"] = Acotar(i.UnidadMedida)
            });

        var datos = ResumenJson(detalle.Resumen, nombresDeObras);
        datos["ok"] = true;
        datos["items"] = items;
        return ResultadoHerramienta.Bien(datos);
    }

    private async Task<ResultadoHerramienta> ListarCotizacionesAsync(string argumentosJson, CancellationToken ct)
    {
        if (!TryLeerId(argumentosJson, out var id))
            return ResultadoHerramienta.Error("argumentos_invalidos");

        var resultado = await _licitaciones.ListarCotizacionesAsync(id, MaxCotizaciones, ct);
        if (resultado.Error is not null)
            return ResultadoHerramienta.Error(resultado.Error);

        // De mayor a menor puntaje, para que se lean en orden. Las cifras no se tocan.
        var ofertas = resultado.Cotizaciones!
            .Take(MaxCotizaciones)
            .OrderByDescending(c => c.PuntajeTotal)
            .ToList();

        var lista = new JsonArray();
        foreach (var c in ofertas)
            lista.Add(CotizacionJson(c));

        var total = resultado.Total ?? ofertas.Count;
        var datos = new JsonObject
        {
            ["ok"] = true,
            ["licitacionId"] = id.ToString(),
            ["total"] = total,
            ["mostradas"] = lista.Count,
            ["hayMas"] = total > lista.Count,
            ["cotizaciones"] = lista
        };
        return ResultadoHerramienta.Bien(datos);
    }

    private static JsonObject CotizacionJson(CotizacionResumen c) => new()
    {
        ["cotizacionId"] = c.Id.ToString(),
        // El nombre todavía puede venir null (ver #206): sin nombre, se identifica con un código corto, nunca con un GUID entero.
        ["proveedorNombre"] = Acotar(c.ProveedorNombre),
        ["proveedorCodigo"] = c.ProveedorId.ToString("N")[..8],
        ["precioMateriales"] = c.SubtotalItems,
        ["flete"] = c.Flete,
        ["costoTotal"] = c.CostoTotal,
        ["plazoEntregaDias"] = c.PlazoEntregaDias,
        ["puntajeTotal"] = c.PuntajeTotal,
        ["esGanadora"] = c.EsGanadora,
        ["esMasBarata"] = c.EsMasBarata,
        // null = la API no devolvió la calificación del proveedor: el modelo no la inventa.
        ["calificacionProveedor"] = c.Calificacion
    };

    /// <summary>Las obras se piden una vez para mostrar el nombre. Si falla, se sigue sin nombre (no se corta la consulta).</summary>
    private async Task<Dictionary<Guid, string>> NombresDeObrasAsync(CancellationToken ct)
    {
        var resultado = await _obras.ListarAsync(ct);
        if (resultado.Error is not null || resultado.Obras is null)
            return new Dictionary<Guid, string>();
        return resultado.Obras
            .GroupBy(o => o.Id)
            .ToDictionary(g => g.Key, g => g.First().Nombre);
    }

    private static JsonObject ResumenJson(LicitacionResumen l, IReadOnlyDictionary<Guid, string> obras) => new()
    {
        ["licitacionId"] = l.Id.ToString(),
        ["numeroRequisicion"] = Acotar(l.NumeroRequisicion),
        ["estado"] = l.Estado,
        ["obra"] = obras.TryGetValue(l.ObraId, out var nombre) ? nombre : null,
        // La API la guarda en UTC: se la pasamos así y las instrucciones piden no inventar la hora local.
        ["fechaLimiteCotizacionUtc"] = l.FechaLimiteCotizacion.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm'Z'", System.Globalization.CultureInfo.InvariantCulture),
        ["cantidadItems"] = l.CantidadItems
    };

    /// <summary>Recorta los bordes, cambia los caracteres de control por espacios y corta a MaxTextoLibre. Null si queda vacío.</summary>
    private static string? Acotar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;
        var limpio = new string(texto.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        if (limpio.Length == 0)
            return null;
        return limpio.Length <= MaxTextoLibre ? limpio : limpio[..MaxTextoLibre];
    }

    private static bool TryLeerId(string argumentosJson, out Guid id)
    {
        id = Guid.Empty;
        if (!TryLeerArgumentos(argumentosJson, out var args))
            return false;
        var texto = LeerTexto(args["licitacionId"]);
        return !string.IsNullOrWhiteSpace(texto) && Guid.TryParse(texto.Trim(), out id) && id != Guid.Empty;
    }

    private static bool TryLeerArgumentos(string argumentosJson, out JsonObject args)
    {
        args = new JsonObject();
        try
        {
            // Un modelo que no manda argumentos manda "" o "{}": ambos valen para herramientas sin parámetros obligatorios.
            if (string.IsNullOrWhiteSpace(argumentosJson))
                return true;
            if (JsonNode.Parse(argumentosJson) is not JsonObject objeto)
                return false;
            args = objeto;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? LeerTexto(JsonNode? nodo) =>
        nodo is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
