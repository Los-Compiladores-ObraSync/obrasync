using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ObraSync.Asistente.Application.Generadas;

/// <summary>Lo que devuelve el cliente de lectura: el cuerpo ya parseado, o un código de error estable.</summary>
public sealed record RespuestaApiLectura(bool Ok, string? Error, JsonNode? Cuerpo, int? Total)
{
    public static RespuestaApiLectura Bien(JsonNode cuerpo, int? total) => new(true, null, cuerpo, total);
    public static RespuestaApiLectura Falla(string error) => new(false, error, null, null);
}

/// <summary>
/// Hace un GET al Gateway con el JWT del usuario (implementación en Infrastructure).
/// Recibe SOLO la ruta y la consulta ya validadas; la dirección base sale de la configuración.
/// </summary>
public interface IClienteApiLectura
{
    Task<RespuestaApiLectura> GetAsync(string rutaYConsulta, CancellationToken ct);
}

/// <summary>
/// Ejecuta las herramientas generadas desde el contrato (#151). Mismas reglas que las escritas a mano:
/// los argumentos se validan ANTES de llamar, la dirección sale de la configuración (el modelo nunca arma una URL),
/// solo GET, las respuestas vienen acotadas por el cliente y los errores tienen códigos estables.
/// </summary>
public sealed class HerramientasGeneradas : IGrupoHerramientas
{
    /// <summary>Tamaño de página si el modelo no lo pide: las respuestas largas gastan tokens del cupo compartido.</summary>
    public const int TamanoPorDefecto = 10;
    public const int TamanoMaximo = 20;
    public const int MaxTextoLibre = 100;

    private static readonly Regex TextoPermitido = new(@"^[\p{L}\p{N} _.\-]+$", RegexOptions.Compiled);

    private readonly IReadOnlyDictionary<string, HerramientaGenerada> _porNombre;
    private readonly IReadOnlyList<HerramientaGenerada> _catalogo;
    private readonly IClienteApiLectura _cliente;

    public HerramientasGeneradas(IReadOnlyList<HerramientaGenerada> catalogo, IClienteApiLectura cliente)
    {
        _catalogo = catalogo;
        _porNombre = catalogo.ToDictionary(h => h.Nombre, StringComparer.Ordinal);
        _cliente = cliente;
    }

    public bool EsGenerada(string nombre) => _porNombre.ContainsKey(nombre);

    public bool Contiene(string nombre) => EsGenerada(nombre);

    public bool PermiteRol(PerfilAsistente perfil, string nombre) =>
        _porNombre.TryGetValue(nombre, out var herramienta) && Para(perfil.Rol).Contains(herramienta);

    IEnumerable<JsonNode> IGrupoHerramientas.DefinicionesPara(PerfilAsistente perfil) =>
        Para(perfil.Rol).Select(h => (JsonNode)Definicion(h));

    Task<ResultadoHerramienta> IGrupoHerramientas.EjecutarAsync(
        PerfilAsistente perfil, string nombre, string argumentosJson, CancellationToken ct) =>
        EjecutarAsync(perfil.Rol, nombre, argumentosJson, ct);

    /// <summary>
    /// Lo que ese rol puede llamar según el contrato. Solo los roles que tienen asistente (Capataz, Comprador, Admin):
    /// el Proveedor figura en algunos contratos, pero no usa el asistente (decisión pendiente en #150).
    /// </summary>
    public IReadOnlyList<HerramientaGenerada> Para(string? rol) =>
        PerfilAsistente.Para(rol) is null ? Array.Empty<HerramientaGenerada>() : _catalogo.Where(h => h.Roles.Contains(rol!)).ToList();

    /// <summary>Las definiciones (formato de herramientas del modelo) de lo que ese rol puede llamar según el contrato.</summary>
    public JsonArray DefinicionesPara(string? rol)
    {
        var defs = new JsonArray();
        foreach (var h in Para(rol))
            defs.Add(Definicion(h));
        return defs;
    }

    private static JsonObject Definicion(HerramientaGenerada h)
    {
        var propiedades = new JsonObject();
        var requeridos = new JsonArray();
        foreach (var p in h.Parametros)
        {
            var propiedad = new JsonObject { ["type"] = p.Tipo };
            if (p.Formato == "uuid")
                propiedad["format"] = "uuid";
            if (p.Valores is { Count: > 0 })
                propiedad["enum"] = new JsonArray(p.Valores.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());
            if (p.Tipo == "integer")
            {
                if (p.Minimo is { } min) propiedad["minimum"] = (int)min;
                var max = MaximoEfectivo(p);
                if (max is { } mx) propiedad["maximum"] = (int)mx;
            }
            if (p.Descripcion.Length > 0)
                propiedad["description"] = p.Descripcion;
            propiedades[p.Nombre] = propiedad;
            if (p.Requerido)
                requeridos.Add(p.Nombre);
        }

        return new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = h.Nombre,
                ["description"] = h.Descripcion,
                ["parameters"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = propiedades,
                    ["required"] = requeridos,
                    ["additionalProperties"] = false
                }
            }
        };
    }

    /// <summary>El tope de "tamano" es el menor entre el del contrato y el nuestro.</summary>
    private static decimal? MaximoEfectivo(ParametroGenerado p) =>
        p.Nombre == "tamano" ? Math.Min(p.Maximo ?? TamanoMaximo, TamanoMaximo) : p.Maximo;

    public async Task<ResultadoHerramienta> EjecutarAsync(string? rol, string nombre, string argumentosJson, CancellationToken ct)
    {
        if (!_porNombre.TryGetValue(nombre, out var herramienta))
            return ResultadoHerramienta.Error("herramienta_desconocida");
        // Defensa extra: aunque el modelo pida una herramienta que no se le mostró, si el rol no figura en el contrato no se ejecuta.
        if (!Para(rol).Contains(herramienta))
            return ResultadoHerramienta.Error("herramienta_no_permitida");

        var (rutaYConsulta, error) = ArmarPedido(herramienta, argumentosJson);
        if (error is not null)
            return error;

        var r = await _cliente.GetAsync(rutaYConsulta!, ct);
        if (!r.Ok)
            return ResultadoHerramienta.Error(r.Error ?? "api_http_error");

        var datos = new JsonObject { ["ok"] = true, ["datos"] = r.Cuerpo };
        if (r.Total is { } total)
            datos["total"] = total;
        return ResultadoHerramienta.Bien(datos);
    }

    private static (string? Ruta, ResultadoHerramienta? Error) ArmarPedido(HerramientaGenerada h, string argumentosJson)
    {
        JsonObject args;
        try
        {
            args = string.IsNullOrWhiteSpace(argumentosJson) ? new JsonObject() : JsonNode.Parse(argumentosJson) as JsonObject
                   ?? throw new JsonException();
        }
        catch (JsonException)
        {
            return (null, ResultadoHerramienta.Error("argumentos_invalidos"));
        }

        // Un argumento que el contrato no declara es un error: no se ignora en silencio.
        foreach (var (clave, _) in args)
            if (h.Parametros.All(p => p.Nombre != clave))
                return (null, Invalido(clave));

        var ruta = h.Ruta;
        var consulta = new List<string>();
        foreach (var p in h.Parametros)
        {
            args.TryGetPropertyValue(p.Nombre, out var valor);
            if (valor is null)
            {
                if (p.Requerido)
                    return (null, Invalido(p.Nombre));
                if (p.Nombre == "tamano")
                    consulta.Add($"tamano={TamanoPorDefecto}");
                continue;
            }

            var texto = Convertir(p, valor);
            if (texto is null)
                return (null, Invalido(p.Nombre));

            var codificado = Uri.EscapeDataString(texto);
            if (p.Ubicacion == ParametroGenerado.EnRuta)
                ruta = ruta.Replace("{" + p.Nombre + "}", codificado, StringComparison.Ordinal);
            else
                consulta.Add($"{Uri.EscapeDataString(p.Nombre)}={codificado}");
        }

        return (consulta.Count == 0 ? ruta : ruta + "?" + string.Join('&', consulta), null);
    }

    private static ResultadoHerramienta Invalido(string campo) =>
        ResultadoHerramienta.ErrorConDetalle("argumentos_invalidos", "campo", JsonValue.Create(campo)!);

    /// <summary>Valida un argumento contra lo que dice el contrato y lo pasa a texto. Null = inválido.</summary>
    private static string? Convertir(ParametroGenerado p, JsonNode valor)
    {
        if (valor is not JsonValue v)
            return null;

        switch (p.Tipo)
        {
            case "integer":
            {
                if (!v.TryGetValue<long>(out var n))
                    return null;
                if (p.Minimo is { } min && n < min)
                    return null;
                if (MaximoEfectivo(p) is { } max && n > max)
                    return null;
                return n.ToString(CultureInfo.InvariantCulture);
            }
            case "number":
            {
                if (!v.TryGetValue<double>(out var d) || double.IsNaN(d) || double.IsInfinity(d))
                    return null;
                if (p.Minimo is { } min && d < (double)min)
                    return null;
                if (p.Maximo is { } max && d > (double)max)
                    return null;
                return d.ToString("R", CultureInfo.InvariantCulture);
            }
            case "boolean":
                return v.TryGetValue<bool>(out var b) ? (b ? "true" : "false") : null;
            default:
            {
                if (!v.TryGetValue<string>(out var s) || string.IsNullOrWhiteSpace(s))
                    return null;
                s = s.Trim();
                if (p.Formato == "uuid")
                    return Guid.TryParse(s, out var g) ? g.ToString("D") : null;
                if (p.Valores is { Count: > 0 })
                    return p.Valores.Contains(s) ? s : null;
                // Texto libre: corto y solo con letras, números, espacio, guion, punto y guion bajo (nada de / ? & # ..).
                if (s.Length > MaxTextoLibre || !TextoPermitido.IsMatch(s) || s.Contains(".."))
                    return null;
                return s;
            }
        }
    }
}
