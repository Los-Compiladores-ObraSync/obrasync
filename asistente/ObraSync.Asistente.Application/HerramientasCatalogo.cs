using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ObraSync.Asistente.Application;

/// <summary>
/// Resultado de una herramienta: ok + datos, o ok=false + código de error estable.
/// "Borrador" solo lo completa armar_borrador_requisicion; el modelo nunca lo ve entero (ve el resumen).
/// </summary>
public sealed record ResultadoHerramienta(
    bool Ok, JsonNode Datos, BorradorRequisicion? Borrador = null, BorradorAprobacion? Aprobacion = null,
    BorradorRecepcion? Recepcion = null, BorradorAdjudicacion? Adjudicacion = null)
{
    public static ResultadoHerramienta Bien(JsonNode datos) => new(true, datos);
    public static ResultadoHerramienta Error(string codigo) =>
        new(false, new JsonObject { ["ok"] = false, ["error"] = codigo });

    /// <summary>Error con datos útiles para que el modelo le pregunte algo concreto a la persona (opciones, etc.).</summary>
    public static ResultadoHerramienta ErrorConDetalle(string codigo, string clave, JsonNode detalle) =>
        new(false, new JsonObject { ["ok"] = false, ["error"] = codigo, [clave] = detalle });
}

/// <summary>
/// Herramientas del asistente. TODAS son de solo lectura:
///  - buscar_insumos: busca en el catálogo.
///  - listar_mis_obras: lista las obras que el usuario puede ver, con sus etapas (para que no tenga que acordarse del nombre).
///  - armar_borrador_requisicion: lee obras e insumos y ARMA un borrador. No crea nada: la requisición
///    la crea el frontend, con el JWT del usuario, cuando la persona toca Confirmar.
/// Valida los argumentos ANTES de llamar a la API y devuelve errores estables.
/// </summary>
public sealed class HerramientasCatalogo : GrupoHerramientasDePerfil, IHerramientasCatalogo
{
    public const int MaxTextoBusqueda = 60;
    public const int MaxTextoObraEtapa = 100;
    public const int MaxObservaciones = 500;
    public const int MaxItemsBorrador = 10;
    public const int MaxOpciones = 5;

    public const string BuscarInsumos = "buscar_insumos";
    public const string ArmarBorrador = "armar_borrador_requisicion";
    public const string ListarObras = "listar_mis_obras";
    /// <summary>Tope de obras que se le devuelven al modelo: alcanza para un Capataz y cuida el cupo de tokens.</summary>
    public const int MaxObrasListadas = 20;

    public static readonly IReadOnlyList<string> Nombres =
        new[] { BuscarInsumos, ListarObras, ArmarBorrador };

    private readonly IBuscadorInsumos _buscador;
    private readonly IBuscadorObras _obras;

    public HerramientasCatalogo(IBuscadorInsumos buscador, IBuscadorObras obras)
    {
        _buscador = buscador;
        _obras = obras;
    }

    protected override IReadOnlyList<string> HerramientasDelGrupo => Nombres;
    protected override JsonArray ObtenerDefiniciones() => Definiciones();

    /// <summary>Descripción de las herramientas que se le muestran al modelo.</summary>
    public static JsonArray Definiciones() => new()
    {
        new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = BuscarInsumos,
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
        },
        new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = ListarObras,
                ["description"] =
                    "Lista las obras que la persona tiene asignadas (las que puede ver), con el nombre de sus etapas. " +
                    "Usala cuando pregunte qué obras tiene, qué obras hay o si no se acuerda del nombre de una obra. Solo lectura.",
                ["parameters"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }
            }
        },
        new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = ArmarBorrador,
                ["description"] =
                    "Arma un BORRADOR de requisición para que la persona lo revise y confirme. No crea nada. " +
                    "Resuelve la obra, la etapa y los insumos a partir de sus nombres. " +
                    "Si falta o es ambiguo algún dato, devuelve un error con opciones para preguntarle a la persona.",
                ["parameters"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["obra"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Nombre de la obra."
                        },
                        ["etapa"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Nombre de la etapa de la obra, por ejemplo: Estructura."
                        },
                        ["observaciones"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Nota opcional de la persona."
                        },
                        ["items"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["description"] = "Materiales pedidos (máximo 10).",
                            ["items"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JsonObject
                                {
                                    ["insumo"] = new JsonObject
                                    {
                                        ["type"] = "string",
                                        ["description"] = "Nombre o código del insumo, por ejemplo: cemento"
                                    },
                                    ["cantidad"] = new JsonObject
                                    {
                                        ["type"] = "number",
                                        ["description"] = "Cantidad, mayor a cero."
                                    }
                                },
                                ["required"] = new JsonArray("insumo", "cantidad")
                            }
                        }
                    },
                    ["required"] = new JsonArray("obra", "etapa", "items")
                }
            }
        }
    };

    /// <summary>Ejecuta una herramienta pedida por el modelo.</summary>
    public override Task<ResultadoHerramienta> EjecutarAsync(string nombre, string argumentosJson, CancellationToken ct) =>
        nombre switch
        {
            BuscarInsumos => BuscarInsumosAsync(argumentosJson, ct),
            ArmarBorrador => ArmarBorradorAsync(argumentosJson, ct),
            ListarObras => ListarObrasAsync(argumentosJson, ct),
            _ => Task.FromResult(ResultadoHerramienta.Error("herramienta_desconocida"))
        };

    /// <summary>
    /// Devuelve las obras que el usuario puede ver (la API ya filtra por rol: un Capataz solo ve las suyas) y sus etapas.
    /// No recibe argumentos. Si hay más que el tope, lo avisa con hayMas para que el modelo no afirme que son todas.
    /// </summary>
    private async Task<ResultadoHerramienta> ListarObrasAsync(string argumentosJson, CancellationToken ct)
    {
        try
        {
            var args = JsonNode.Parse(string.IsNullOrWhiteSpace(argumentosJson) ? "{}" : argumentosJson);
            if (args is not JsonObject)
                return ResultadoHerramienta.Error("argumentos_invalidos");
        }
        catch (JsonException)
        {
            return ResultadoHerramienta.Error("argumentos_invalidos");
        }

        var resultado = await _obras.ListarAsync(ct);
        if (resultado.Error is not null || resultado.Obras is null)
            return ResultadoHerramienta.Error(resultado.Error ?? "obras_no_disponibles");

        var lista = new JsonArray();
        foreach (var obra in resultado.Obras.Take(MaxObrasListadas))
        {
            var etapas = new JsonArray();
            foreach (var etapa in obra.Etapas)
                etapas.Add(etapa);
            lista.Add(new JsonObject { ["nombre"] = obra.Nombre, ["etapas"] = etapas });
        }

        return ResultadoHerramienta.Bien(new JsonObject
        {
            ["ok"] = true,
            ["total"] = resultado.Obras.Count,
            ["hayMas"] = resultado.Obras.Count > MaxObrasListadas,
            ["obras"] = lista
        });
    }

    private async Task<ResultadoHerramienta> BuscarInsumosAsync(string argumentosJson, CancellationToken ct)
    {
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

    private sealed record ItemPedido(string Insumo, double Cantidad);

    private async Task<ResultadoHerramienta> ArmarBorradorAsync(string argumentosJson, CancellationToken ct)
    {
        // 1) Validar los argumentos SIN llamar a ninguna API.
        JsonObject? args;
        try
        {
            args = JsonNode.Parse(argumentosJson) as JsonObject;
        }
        catch (JsonException)
        {
            return ResultadoHerramienta.Error("argumentos_invalidos");
        }
        if (args is null)
            return ResultadoHerramienta.Error("argumentos_invalidos");

        var textoObra = LeerTexto(args["obra"]);
        var textoEtapa = LeerTexto(args["etapa"]);
        var observaciones = LeerTexto(args["observaciones"]);

        if ((textoObra?.Length ?? 0) > MaxTextoObraEtapa ||
            (textoEtapa?.Length ?? 0) > MaxTextoObraEtapa ||
            (observaciones?.Length ?? 0) > MaxObservaciones)
            return ResultadoHerramienta.Error("argumentos_invalidos");

        if (args["items"] is not JsonArray itemsArg || itemsArg.Count == 0 || itemsArg.Count > MaxItemsBorrador)
            return ResultadoHerramienta.Error("argumentos_invalidos");

        var pedidos = new List<ItemPedido>();
        foreach (var nodo in itemsArg)
        {
            // Cada ítem tiene que ser un objeto {insumo, cantidad}; cualquier otra cosa se rechaza.
            if (nodo is not JsonObject item)
                return ResultadoHerramienta.Error("argumentos_invalidos");
            var insumo = LeerTexto(item["insumo"]);
            if (string.IsNullOrWhiteSpace(insumo) || insumo.Length > MaxTextoBusqueda)
                return ResultadoHerramienta.Error("argumentos_invalidos");
            if (!TryLeerCantidad(item["cantidad"], out var cantidad))
                return ResultadoHerramienta.Error("cantidad_invalida");
            pedidos.Add(new ItemPedido(insumo.Trim(), cantidad));
        }

        // 2) Obra y etapa (la API ya filtra: un Capataz solo ve sus obras).
        var resObras = await _obras.ListarAsync(ct);
        if (resObras.Error is not null)
            return ResultadoHerramienta.Error(resObras.Error);
        var obras = resObras.Obras!;

        if (string.IsNullOrWhiteSpace(textoObra))
            return ResultadoHerramienta.ErrorConDetalle("obra_requerida", "obras", OpcionesJson(obras.Select(o => o.Nombre)));

        var obrasCoinciden = Coincidencias(obras, o => o.Nombre, textoObra);
        if (obrasCoinciden.Count == 0)
            return ResultadoHerramienta.ErrorConDetalle("obra_no_encontrada", "obras", OpcionesJson(obras.Select(o => o.Nombre)));
        if (obrasCoinciden.Count > 1)
            return ResultadoHerramienta.ErrorConDetalle("obra_ambigua", "obras", OpcionesJson(obrasCoinciden.Select(o => o.Nombre)));
        var obra = obrasCoinciden[0];

        string? etapa = null;
        if (obra.Etapas.Count > 0)
        {
            if (string.IsNullOrWhiteSpace(textoEtapa))
                return ResultadoHerramienta.ErrorConDetalle("etapa_requerida", "etapas", OpcionesJson(obra.Etapas));
            var etapasCoinciden = Coincidencias(obra.Etapas, e => e, textoEtapa);
            if (etapasCoinciden.Count == 0)
                return ResultadoHerramienta.ErrorConDetalle("etapa_no_encontrada", "etapas", OpcionesJson(obra.Etapas));
            if (etapasCoinciden.Count > 1)
                return ResultadoHerramienta.ErrorConDetalle("etapa_ambigua", "etapas", OpcionesJson(etapasCoinciden));
            etapa = etapasCoinciden[0];
        }

        // 3) Insumos: cada nombre se resuelve contra el catálogo REAL. El modelo no inventa ids.
        var items = new List<BorradorItem>();
        var resumenItems = new List<BorradorResumenItem>();
        var problemas = new JsonArray();
        var usados = new HashSet<Guid>();

        foreach (var pedido in pedidos)
        {
            var busqueda = await _buscador.BuscarAsync(pedido.Insumo, ct);
            if (busqueda.Error is not null)
                return ResultadoHerramienta.Error(busqueda.Error);

            var encontrados = busqueda.Insumos!.Where(i => Guid.TryParse(i.ArticuloId, out _)).ToList();
            var exactos = encontrados
                .Where(i => Iguales(i.Descripcion, pedido.Insumo) || Iguales(i.Codigo, pedido.Insumo))
                .ToList();
            var candidatos = exactos.Count > 0 ? exactos : encontrados;

            if (candidatos.Count == 0)
            {
                problemas.Add(new JsonObject { ["insumo"] = pedido.Insumo, ["error"] = "insumo_no_encontrado" });
                continue;
            }
            if (candidatos.Count > 1)
            {
                var opciones = new JsonArray();
                foreach (var c in candidatos.Take(MaxOpciones))
                    opciones.Add($"{c.Descripcion} ({c.Codigo})");
                problemas.Add(new JsonObject
                {
                    ["insumo"] = pedido.Insumo,
                    ["error"] = "insumo_ambiguo",
                    ["opciones"] = opciones
                });
                continue;
            }

            var elegido = candidatos[0];
            var articuloId = Guid.Parse(elegido.ArticuloId);
            if (!usados.Add(articuloId))
            {
                problemas.Add(new JsonObject { ["insumo"] = pedido.Insumo, ["error"] = "insumo_repetido" });
                continue;
            }
            items.Add(new BorradorItem(articuloId, pedido.Cantidad));
            resumenItems.Add(new BorradorResumenItem(elegido.Descripcion, pedido.Cantidad, elegido.UnidadMedida));
        }

        if (problemas.Count > 0)
            return ResultadoHerramienta.ErrorConDetalle("items_invalidos", "problemas", problemas);

        var borrador = new BorradorRequisicion(
            BorradorRequisicion.AccionCrearRequisicion,
            "Nueva requisición",
            new BorradorResumen(obra.Nombre, etapa, resumenItems),
            new BorradorCrearRequisicion(
                obra.Id,
                etapa,
                string.IsNullOrWhiteSpace(observaciones) ? null : observaciones.Trim(),
                items));

        // Lo que ve el modelo: solo el resumen (nombres y cantidades), sin ids.
        var resumenParaModelo = new JsonArray();
        foreach (var r in resumenItems)
            resumenParaModelo.Add(new JsonObject
            {
                ["descripcion"] = r.Descripcion,
                ["cantidad"] = r.Cantidad,
                ["unidadMedida"] = r.UnidadMedida
            });

        var datos = new JsonObject
        {
            ["ok"] = true,
            ["borradorArmado"] = true,
            ["obra"] = obra.Nombre,
            ["etapa"] = etapa,
            ["items"] = resumenParaModelo,
            ["nota"] = "Todavía no se creó nada: la persona debe revisar el borrador y tocar Confirmar."
        };
        return new ResultadoHerramienta(true, datos, borrador);
    }

    private static string? LeerTexto(JsonNode? nodo) =>
        nodo is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>Acepta número o texto numérico ("10"), porque los modelos a veces mandan cualquiera de los dos.</summary>
    private static bool TryLeerCantidad(JsonNode? nodo, out double cantidad)
    {
        cantidad = 0;
        if (nodo is not JsonValue v)
            return false;

        if (!v.TryGetValue<double>(out cantidad))
        {
            if (!v.TryGetValue<string>(out var texto) ||
                !double.TryParse(texto, NumberStyles.Float, CultureInfo.InvariantCulture, out cantidad))
                return false;
        }
        return double.IsFinite(cantidad) && cantidad > 0;
    }

    private static bool Iguales(string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Primero coincidencia exacta; si no hay, las que contienen el texto.</summary>
    private static List<T> Coincidencias<T>(IEnumerable<T> fuente, Func<T, string> nombre, string texto)
    {
        var lista = fuente.ToList();
        var exactas = lista.Where(x => Iguales(nombre(x), texto)).ToList();
        if (exactas.Count > 0)
            return exactas;
        return lista.Where(x => nombre(x).Contains(texto.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private static JsonArray OpcionesJson(IEnumerable<string> nombres)
    {
        var arr = new JsonArray();
        foreach (var n in nombres.Take(MaxOpciones))
            arr.Add(n);
        return arr;
    }
}
