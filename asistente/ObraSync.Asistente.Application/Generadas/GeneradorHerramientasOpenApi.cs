using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ObraSync.Asistente.Application.Generadas;

/// <summary>Un parámetro (de la ruta o de la consulta) de un GET del contrato OpenAPI.</summary>
public sealed record ParametroGenerado(
    string Nombre, string Ubicacion, bool Requerido, string Tipo, string? Formato,
    decimal? Minimo, decimal? Maximo, IReadOnlyList<string>? Valores, string Descripcion)
{
    public const string EnRuta = "path";
    public const string EnConsulta = "query";
}

/// <summary>Una herramienta de SOLO LECTURA sacada de un GET del contrato.</summary>
public sealed record HerramientaGenerada(
    string Nombre, string Descripcion, string Ruta, IReadOnlyList<ParametroGenerado> Parametros, IReadOnlyList<string> Roles);

/// <summary>
/// Convertidor API → herramientas (#151): lee los contratos OpenAPI (contracts/openapi) y arma una herramienta por cada GET.
/// Es una función pura (texto JSON adentro, lista afuera): no llama a nadie, así se prueba fácil.
/// Solo mira los GET: las escrituras nunca se generan (van a mano, detrás del botón Confirmar, #146).
/// </summary>
public static class GeneradorHerramientasOpenApi
{
    public const int MaxLongitudNombre = 64;

    /// <summary>Roles que existen en ObraSync. Solo estos se reconocen al leer "(Roles: ...)" del contrato.</summary>
    public static readonly string[] RolesConocidos = { "Capataz", "Comprador", "Proveedor", "Admin" };

    /// <summary>
    /// GET que NO se generan, cada uno con su motivo. Los que ya tienen herramienta escrita a mano se dejan afuera
    /// porque la escrita a mano devuelve una respuesta resumida (menos tokens) y con reglas propias.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Excluidos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["/api/ordenes-compra/precios-historicos"] = "recibe una lista de artículos (parámetro de tipo array), que el generador no soporta; lo usa la pantalla de Compras",
        ["/api/licitaciones/magic-links/{hash}"] = "el hash es un secreto de un solo uso: el modelo no debe poder consultarlo",
        ["/api/asistente/estado"] = "es del propio asistente (consumo de tokens) y es solo del Admin",
        ["/api/requisiciones"] = "ya existe listar_requisiciones",
        ["/api/requisiciones/{id}"] = "ya existe ver_requisicion",
        ["/api/catalogo/insumos"] = "ya existe buscar_insumos",
        ["/api/licitaciones"] = "ya existe listar_licitaciones",
        ["/api/licitaciones/{id}"] = "ya existe ver_licitacion",
        ["/api/cotizaciones"] = "ya existe listar_cotizaciones",
        ["/api/recepciones/ordenes-pendientes"] = "ya existe listar_ordenes_pendientes",
        ["/api/presupuestos/partidas"] = "ya existe consultar_partidas_presupuestarias",
        ["/api/recepciones/ordenes-bloqueadas"] = "ya existe la herramienta de órdenes bloqueadas (#210)",
    };

    public static IReadOnlyList<HerramientaGenerada> Generar(IEnumerable<string> contratosJson)
    {
        var todas = new List<HerramientaGenerada>();
        foreach (var json in contratosJson)
            todas.AddRange(Generar(json));

        var repetido = todas.GroupBy(h => h.Nombre).FirstOrDefault(g => g.Count() > 1);
        if (repetido is not null)
            throw new InvalidOperationException($"Dos GET generan el mismo nombre de herramienta: {repetido.Key}.");

        return todas.OrderBy(h => h.Nombre, StringComparer.Ordinal).ToList();
    }

    public static IReadOnlyList<HerramientaGenerada> Generar(string contratoJson)
    {
        var raiz = JsonNode.Parse(contratoJson, documentOptions: new System.Text.Json.JsonDocumentOptions { AllowTrailingCommas = true })
                   ?? throw new InvalidOperationException("El contrato OpenAPI está vacío.");
        var paths = raiz["paths"] as JsonObject ?? throw new InvalidOperationException("El contrato no tiene 'paths'.");

        var lista = new List<HerramientaGenerada>();
        foreach (var (ruta, item) in paths)
        {
            if (item is not JsonObject metodos || Excluidos.ContainsKey(ruta))
                continue;
            if (!TryObtenerGet(metodos, out var get))
                continue;

            var resumen = get["summary"]?.GetValue<string>() ?? "";
            var descripcion = get["description"]?.GetValue<string>() ?? "";
            if (resumen.Contains("AllowAnonymous", StringComparison.OrdinalIgnoreCase) ||
                resumen.Contains("Público", StringComparison.OrdinalIgnoreCase))
                continue; // sin sesión: no es para el asistente

            var parametros = LeerParametros(get, ruta);
            var roles = LeerRoles(resumen, descripcion);
            lista.Add(new HerramientaGenerada(NombreDe(ruta), DescripcionDe(resumen, ruta), ruta, parametros, roles));
        }
        return lista;
    }

    private static bool TryObtenerGet(JsonObject metodos, out JsonObject get)
    {
        foreach (var (metodo, valor) in metodos)
        {
            if (metodo.Equals("get", StringComparison.OrdinalIgnoreCase) && valor is JsonObject o)
            {
                get = o;
                return true;
            }
        }
        get = null!;
        return false;
    }

    /// <summary>consultar_ + partes fijas de la ruta (sin "api") + _por_ + parámetros de la ruta. Ej.: consultar_cotizaciones_proveedores_calificacion_por_proveedorid.</summary>
    public static string NombreDe(string ruta)
    {
        var partes = ruta.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !p.Equals("api", StringComparison.OrdinalIgnoreCase)).ToList();
        var fijas = partes.Where(p => !p.StartsWith('{')).Select(Normalizar);
        var enRuta = partes.Where(p => p.StartsWith('{')).Select(p => Normalizar(p.Trim('{', '}')));

        var nombre = "consultar_" + string.Join('_', fijas);
        if (enRuta.Any())
            nombre += "_por_" + string.Join("_y_", enRuta);

        if (nombre.Length > MaxLongitudNombre)
            throw new InvalidOperationException($"El nombre generado para {ruta} supera {MaxLongitudNombre} caracteres: {nombre}.");
        return nombre;
    }

    private static string Normalizar(string texto) =>
        Regex.Replace(texto.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');

    private static string DescripcionDe(string resumen, string ruta)
    {
        // Se saca "(Roles: ...)" porque los roles ya filtran qué herramientas se ofrecen: sobran tokens.
        var limpio = Regex.Replace(resumen, @"\s*\((Roles|Público)[^)]*\)", "").Trim();
        if (limpio.Length == 0)
            limpio = $"Consulta {ruta}";
        return limpio + ". Solo lectura.";
    }

    private static IReadOnlyList<ParametroGenerado> LeerParametros(JsonObject get, string ruta)
    {
        var resultado = new List<ParametroGenerado>();
        if (get["parameters"] is not JsonArray parametros)
            return resultado;

        foreach (var nodo in parametros)
        {
            if (nodo is not JsonObject p)
                continue;
            if (p["$ref"] is not null)
                throw new InvalidOperationException($"{ruta}: los parámetros con $ref no están soportados todavía.");

            var ubicacion = p["in"]?.GetValue<string>() ?? "";
            if (ubicacion is not (ParametroGenerado.EnRuta or ParametroGenerado.EnConsulta))
                continue; // header o cookie: los pone el sistema, no el modelo

            var nombre = p["name"]?.GetValue<string>() ?? throw new InvalidOperationException($"{ruta}: un parámetro no tiene nombre.");
            var esquema = p["schema"] as JsonObject ?? new JsonObject();
            var tipo = esquema["type"]?.GetValue<string>() ?? "string";
            if (tipo is not ("string" or "integer" or "number" or "boolean"))
                throw new InvalidOperationException($"{ruta}: el parámetro {nombre} tiene un tipo no soportado ({tipo}).");

            IReadOnlyList<string>? valores = null;
            if (esquema["enum"] is JsonArray e)
                valores = e.Select(v => v?.ToString() ?? "").Where(v => v.Length > 0).ToList();

            var requerido = ubicacion == ParametroGenerado.EnRuta || (p["required"]?.GetValue<bool>() ?? false);
            var descripcion = p["description"]?.GetValue<string>() ?? "";
            resultado.Add(new ParametroGenerado(
                nombre, ubicacion, requerido, tipo, esquema["format"]?.GetValue<string>(),
                ComoDecimal(esquema["minimum"]), ComoDecimal(esquema["maximum"]), valores,
                descripcion.Length > 120 ? descripcion[..120] : descripcion));
        }
        return resultado;
    }

    private static decimal? ComoDecimal(JsonNode? nodo) =>
        nodo is JsonValue v && v.TryGetValue<decimal>(out var d) ? d : null;

    /// <summary>
    /// Saca los roles del texto del contrato: "(Roles: A, B)" en el resumen o "Roles permitidos: A y B" en la descripción.
    /// Si el contrato no los declara, queda SOLO Admin: ante la duda no se ofrece (el permiso real igual lo valida cada API).
    /// </summary>
    public static IReadOnlyList<string> LeerRoles(string resumen, string descripcion)
    {
        foreach (var texto in new[] { resumen, descripcion })
        {
            var m = Regex.Match(texto, @"Roles(?: permitidos)?:\s*([^().]*)");
            if (!m.Success)
                continue;
            var roles = RolesConocidos.Where(r => Regex.IsMatch(m.Groups[1].Value, $@"\b{r}\b")).ToList();
            if (roles.Count > 0)
                return roles;
        }
        return new[] { "Admin" };
    }
}
