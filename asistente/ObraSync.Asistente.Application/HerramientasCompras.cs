using System.Text.Json;
using System.Text.Json.Nodes;

namespace ObraSync.Asistente.Application;

/// <summary>
/// Herramientas del perfil Comprador (tarjeta #141). TODAS son de solo lectura:
///  - listar_requisiciones: las requisiciones más nuevas, con su estado, y cuántas hay en total.
///  - ver_requisicion: el detalle de una (materiales, observaciones, motivo de rechazo).
///  - preparar_aprobacion_requisicion: ARMA un borrador para aprobar una requisición Pendiente. No aprueba nada:
///    el frontend llama a POST /api/requisiciones/{id}/aprobaciones, con el JWT del Comprador, cuando él toca Confirmar.
///  - preparar_rechazo_requisicion (#149): igual, pero para rechazar, con el motivo que dijo el Comprador. No rechaza nada:
///    el frontend llama a POST /api/requisiciones/{id}/rechazos con ese motivo recién cuando él toca Confirmar.
///  - preparar_publicacion_licitacion (#149): igual, para publicar la licitación de una requisición Aprobada, con los días
///    que tienen los proveedores para cotizar. No publica nada: el frontend llama a POST /api/licitaciones al confirmar.
/// Igual que HerramientasCatalogo, validan los argumentos ANTES de llamar a la API y devuelven errores estables.
/// El texto que viene de la base (observaciones, descripciones) se pasa como dato: las instrucciones del agente
/// le dicen al modelo que ignore cualquier orden que aparezca ahí.
/// </summary>
public sealed class HerramientasCompras
{
    public const string ListarRequisiciones = "listar_requisiciones";
    public const string VerRequisicion = "ver_requisicion";
    public const string PrepararAprobacion = "preparar_aprobacion_requisicion";
    public const string PrepararRechazo = "preparar_rechazo_requisicion";
    public const string PrepararPublicacion = "preparar_publicacion_licitacion";
    /// <summary>Presupuesto de una obra (partidas por etapa) y órdenes de compra bloqueadas por superar la partida. Solo lectura.</summary>
    public const string ConsultarPresupuesto = "consultar_presupuesto_obra";

    /// <summary>El modelo no necesita ver 50 requisiciones: se piden ya acotadas.</summary>
    public const int MaxRequisiciones = 5;
    public const int MaxTextoLibre = 200;

    /// <summary>El mismo máximo que valida la API de Requisiciones para el motivo de rechazo (contrato: 1 a 500).</summary>
    public const int MaxMotivo = 500;

    /// <summary>
    /// Plazo para cotizar, en días. El backend solo exige que la fecha sea futura; este tope es una barrera contra un
    /// número disparatado que escriba el modelo (la persona lo ve en la tarjeta antes de confirmar).
    /// </summary>
    public const int MinDiasPlazo = 1;
    public const int MaxDiasPlazo = 30;

    /// <summary>Estados que acepta la API (compara sin distinguir mayúsculas).</summary>
    public static readonly IReadOnlyList<string> Estados = new[] { "Pendiente", "Aprobada", "Rechazada" };

    public static readonly IReadOnlyList<string> Nombres =
        new[] { ListarRequisiciones, VerRequisicion, PrepararAprobacion, PrepararRechazo, PrepararPublicacion, ConsultarPresupuesto };

    private readonly IConsultorRequisiciones _requisiciones;
    private readonly IBuscadorObras _obras;

    // Opcional: sin ella la herramienta de presupuesto responde "herramienta_no_disponible" (no se rompe nada más).
    private readonly HerramientasPresupuestos? _presupuestos;

    public HerramientasCompras(
        IConsultorRequisiciones requisiciones,
        IBuscadorObras obras,
        HerramientasPresupuestos? presupuestos = null)
    {
        _requisiciones = requisiciones;
        _obras = obras;
        _presupuestos = presupuestos;
    }

    /// <summary>Descripción de las herramientas que se le muestran al modelo.</summary>
    public static JsonArray Definiciones() => new()
    {
        new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = ListarRequisiciones,
                ["description"] =
                    "Lista las requisiciones de materiales más nuevas (máximo 5) y dice cuántas hay en total. " +
                    "Se puede filtrar por estado. Si hayMas es true, hay más requisiciones que las que devolvió: " +
                    "decile a la persona cuántas hay en total, que le mostrás las más nuevas y que puede pedirte un estado en particular. Solo lectura.",
                ["parameters"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["estado"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JsonArray("Pendiente", "Aprobada", "Rechazada"),
                            ["description"] = "Opcional. Pendiente es la que espera aprobación."
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
                ["name"] = VerRequisicion,
                ["description"] =
                    "Muestra el detalle de una requisición: obra, etapa, materiales con cantidades, observaciones y motivo de rechazo. " +
                    "Se pide con el requisicionId que devolvió listar_requisiciones. Solo lectura.",
                ["parameters"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["requisicionId"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Identificador (GUID) de la requisición, tal como lo devolvió listar_requisiciones."
                        }
                    },
                    ["required"] = new JsonArray("requisicionId")
                }
            }
        },
        new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = PrepararAprobacion,
                ["description"] =
                    "Arma el BORRADOR para aprobar una requisición que está Pendiente. No aprueba nada: la persona lo revisa y toca Confirmar. " +
                    "Se pide con el requisicionId que devolvió listar_requisiciones.",
                ["parameters"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["requisicionId"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Identificador (GUID) de la requisición a aprobar."
                        }
                    },
                    ["required"] = new JsonArray("requisicionId")
                }
            }
        },
        new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = PrepararRechazo,
                ["description"] =
                    "Arma el BORRADOR para rechazar una requisición que está Pendiente. No rechaza nada: la persona lo revisa y toca Confirmar. " +
                    "Necesita el motivo del rechazo, que lo dice el Comprador: si no lo dijo, preguntáselo antes de usar esta herramienta. " +
                    "Se pide con el requisicionId que devolvió listar_requisiciones.",
                ["parameters"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["requisicionId"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Identificador (GUID) de la requisición a rechazar."
                        },
                        ["motivo"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["maxLength"] = MaxMotivo,
                            ["description"] = "Por qué se rechaza, con las palabras del Comprador. Obligatorio, hasta 500 caracteres."
                        }
                    },
                    ["required"] = new JsonArray("requisicionId", "motivo")
                }
            }
        },
        new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = PrepararPublicacion,
                ["description"] =
                    "Arma el BORRADOR para publicar la licitación de una requisición que está Aprobada, para que los proveedores coticen. " +
                    "No publica nada: la persona lo revisa (ve hasta qué día pueden cotizar) y toca Confirmar. " +
                    "Se pide con el requisicionId que devolvió listar_requisiciones y los días que tienen para cotizar.",
                ["parameters"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["requisicionId"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Identificador (GUID) de la requisición Aprobada."
                        },
                        ["diasParaCotizar"] = new JsonObject
                        {
                            ["type"] = "integer",
                            ["minimum"] = MinDiasPlazo,
                            ["maximum"] = MaxDiasPlazo,
                            ["description"] = "Cuántos días tienen los proveedores para cotizar, contados desde hoy (1 a 30). Si la persona no lo dijo, usá 7."
                        }
                    },
                    ["required"] = new JsonArray("requisicionId", "diasParaCotizar")
                }
            }
        },
        new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = ConsultarPresupuesto,
                ["description"] =
                    "Consulta el presupuesto de una obra: sus partidas por etapa (presupuestado, comprometido, disponible) y las órdenes de compra " +
                    "que quedaron BLOQUEADAS por superar la partida de su etapa. Se pide con el nombre de la obra (y, si la persona quiere, una etapa). " +
                    "Si la obra es ambigua o no existe, devuelve las opciones: preguntale cuál. Solo lectura: las cifras las calcula el sistema.",
                ["parameters"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["obra"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Nombre de la obra, por ejemplo: Torre Alvear."
                        },
                        ["etapa"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Opcional. Nombre de la etapa para filtrar, por ejemplo: Estructura."
                        }
                    },
                    ["required"] = new JsonArray("obra")
                }
            }
        }
    };

    /// <summary>Ejecuta una herramienta pedida por el modelo.</summary>
    public Task<ResultadoHerramienta> EjecutarAsync(string nombre, string argumentosJson, CancellationToken ct) =>
        nombre switch
        {
            ListarRequisiciones => ListarAsync(argumentosJson, ct),
            VerRequisicion => VerAsync(argumentosJson, ct),
            PrepararAprobacion => PrepararAprobacionAsync(argumentosJson, ct),
            PrepararRechazo => PrepararRechazoAsync(argumentosJson, ct),
            PrepararPublicacion => PrepararPublicacionAsync(argumentosJson, ct),
            ConsultarPresupuesto => ConsultarPresupuestoAsync(argumentosJson, ct),
            _ => Task.FromResult(ResultadoHerramienta.Error("herramienta_desconocida"))
        };

    /// <summary>
    /// Resuelve la obra por nombre (la API ya filtra por rol) y delega en la consulta de presupuestos, que es la misma
    /// que usa el servidor MCP: así chat y MCP muestran siempre las mismas cifras.
    /// </summary>
    private async Task<ResultadoHerramienta> ConsultarPresupuestoAsync(string argumentosJson, CancellationToken ct)
    {
        if (_presupuestos is null)
            return ResultadoHerramienta.Error("herramienta_no_disponible");
        if (!TryLeerArgumentos(argumentosJson, out var args))
            return ResultadoHerramienta.Error("argumentos_invalidos");

        var textoObra = Acotar(LeerTexto(args["obra"]));
        var textoEtapa = Acotar(LeerTexto(args["etapa"]));

        var resObras = await _obras.ListarAsync(ct);
        if (resObras.Error is not null || resObras.Obras is null)
            return ResultadoHerramienta.Error(resObras.Error ?? "obras_no_disponibles");

        if (textoObra is null)
            return ResultadoHerramienta.ErrorConDetalle("obra_requerida", "obras", NombresDeObras(resObras.Obras));

        var coinciden = resObras.Obras.Where(o => string.Equals(o.Nombre.Trim(), textoObra, StringComparison.OrdinalIgnoreCase)).ToList();
        if (coinciden.Count == 0)
            coinciden = resObras.Obras.Where(o => o.Nombre.Contains(textoObra, StringComparison.OrdinalIgnoreCase)).ToList();

        if (coinciden.Count == 0)
            return ResultadoHerramienta.ErrorConDetalle("obra_no_encontrada", "obras", NombresDeObras(resObras.Obras));
        if (coinciden.Count > 1)
            return ResultadoHerramienta.ErrorConDetalle("obra_ambigua", "obras", NombresDeObras(coinciden));

        return await _presupuestos.ConsultarAsync(coinciden[0].Id.ToString(), textoEtapa, ct);
    }

    private static JsonArray NombresDeObras(IEnumerable<ObraAsistente> obras)
    {
        var arr = new JsonArray();
        foreach (var o in obras.Take(10))
            arr.Add(o.Nombre);
        return arr;
    }

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

        var resultado = await _requisiciones.ListarAsync(estado, MaxRequisiciones, ct);
        if (resultado.Error is not null)
            return ResultadoHerramienta.Error(resultado.Error);

        var nombresDeObras = await NombresDeObrasAsync(ct);
        var lista = new JsonArray();
        foreach (var r in resultado.Requisiciones!.Take(MaxRequisiciones))
            lista.Add(ResumenJson(r, nombresDeObras));

        var total = resultado.Total ?? resultado.Requisiciones!.Count;
        var datos = new JsonObject
        {
            ["ok"] = true,
            ["total"] = total,
            // El modelo no tiene que adivinar si falta alguna: se lo decimos con un dato.
            ["mostradas"] = lista.Count,
            ["hayMas"] = total > lista.Count,
            ["requisiciones"] = lista
        };
        return ResultadoHerramienta.Bien(datos);
    }

    private async Task<ResultadoHerramienta> VerAsync(string argumentosJson, CancellationToken ct)
    {
        if (!TryLeerArgumentos(argumentosJson, out var args))
            return ResultadoHerramienta.Error("argumentos_invalidos");

        var texto = LeerTexto(args["requisicionId"]);
        if (string.IsNullOrWhiteSpace(texto) || !Guid.TryParse(texto.Trim(), out var id) || id == Guid.Empty)
            return ResultadoHerramienta.Error("argumentos_invalidos");

        var resultado = await _requisiciones.ObtenerAsync(id, ct);
        if (resultado.Error is not null)
            return ResultadoHerramienta.Error(resultado.Error);

        var detalle = resultado.Requisicion!;
        var nombresDeObras = await NombresDeObrasAsync(ct);

        var items = new JsonArray();
        foreach (var i in detalle.Items)
            items.Add(new JsonObject
            {
                ["descripcion"] = Acotar(i.Descripcion),
                ["cantidad"] = i.Cantidad,
                ["unidadMedida"] = i.UnidadMedida
            });

        var datos = ResumenJson(detalle.Resumen, nombresDeObras);
        datos["ok"] = true;
        datos["observaciones"] = Acotar(detalle.Observaciones);
        datos["motivoRechazo"] = Acotar(detalle.MotivoRechazo);
        datos["items"] = items;
        return ResultadoHerramienta.Bien(datos);
    }

    private enum Operacion { Aprobar, Rechazar, Publicar }

    private Task<ResultadoHerramienta> PrepararAprobacionAsync(string argumentosJson, CancellationToken ct) =>
        PrepararAsync(argumentosJson, Operacion.Aprobar, ct);

    private Task<ResultadoHerramienta> PrepararRechazoAsync(string argumentosJson, CancellationToken ct) =>
        PrepararAsync(argumentosJson, Operacion.Rechazar, ct);

    private Task<ResultadoHerramienta> PrepararPublicacionAsync(string argumentosJson, CancellationToken ct) =>
        PrepararAsync(argumentosJson, Operacion.Publicar, ct);

    /// <summary>
    /// Aprobar, rechazar y publicar arman el mismo tipo de borrador (una requisición con su resumen): cambia la acción,
    /// el estado que tiene que tener la requisición (Pendiente para aprobar y rechazar, Aprobada para publicar) y el dato
    /// extra (el motivo al rechazar, los días de plazo al publicar). Todo se valida ANTES de llamar a la API.
    /// </summary>
    private async Task<ResultadoHerramienta> PrepararAsync(string argumentosJson, Operacion operacion, CancellationToken ct)
    {
        if (!TryLeerArgumentos(argumentosJson, out var args))
            return ResultadoHerramienta.Error("argumentos_invalidos");

        var texto = LeerTexto(args["requisicionId"]);
        if (string.IsNullOrWhiteSpace(texto) || !Guid.TryParse(texto.Trim(), out var id) || id == Guid.Empty)
            return ResultadoHerramienta.Error("argumentos_invalidos");

        string? motivo = null;
        if (operacion == Operacion.Rechazar)
        {
            motivo = LimpiarMotivo(LeerTexto(args["motivo"]));
            if (motivo is null)
                return ResultadoHerramienta.Error("motivo_requerido");
            if (motivo.Length > MaxMotivo)
                return ResultadoHerramienta.Error("motivo_demasiado_largo");
        }

        int? dias = null;
        if (operacion == Operacion.Publicar)
        {
            dias = LeerEntero(args["diasParaCotizar"]);
            if (dias is null || dias < MinDiasPlazo || dias > MaxDiasPlazo)
                return ResultadoHerramienta.Error("plazo_invalido");
        }

        // Siempre se vuelve a leer la requisición: el estado lo decide la API, no lo que recuerde el modelo.
        var resultado = await _requisiciones.ObtenerAsync(id, ct);
        if (resultado.Error is not null)
            return ResultadoHerramienta.Error(resultado.Error);

        var detalle = resultado.Requisicion!;
        if (operacion == Operacion.Publicar)
        {
            // Solo se publica la licitación de una requisición ya aprobada.
            if (!string.Equals(detalle.Resumen.Estado, "Aprobada", StringComparison.OrdinalIgnoreCase))
                return ResultadoHerramienta.ErrorConDetalle("requisicion_no_aprobada", "estado", detalle.Resumen.Estado);
        }
        else if (!string.Equals(detalle.Resumen.Estado, "Pendiente", StringComparison.OrdinalIgnoreCase))
            return ResultadoHerramienta.ErrorConDetalle("requisicion_no_pendiente", "estado", detalle.Resumen.Estado);

        var nombresDeObras = await NombresDeObrasAsync(ct);
        nombresDeObras.TryGetValue(detalle.Resumen.ObraId, out var obra);

        var items = detalle.Items
            .Select(i => new AprobacionResumenItem(Acotar(i.Descripcion) ?? "", i.Cantidad, i.UnidadMedida))
            .ToList();
        var resumen = new AprobacionResumen(detalle.Resumen.Numero, obra, detalle.Resumen.Etapa, items);
        var aprobacion = operacion switch
        {
            Operacion.Rechazar => new BorradorAprobacion(
                BorradorAprobacion.AccionRechazarRequisicion,
                $"Rechazar requisición {detalle.Resumen.Numero}",
                detalle.Resumen.Id, resumen, motivo),
            Operacion.Publicar => new BorradorAprobacion(
                BorradorAprobacion.AccionPublicarLicitacion,
                $"Publicar licitación de la requisición {detalle.Resumen.Numero}",
                detalle.Resumen.Id, resumen, DiasParaCotizar: dias),
            _ => new BorradorAprobacion(
                BorradorAprobacion.AccionAprobarRequisicion,
                $"Aprobar requisición {detalle.Resumen.Numero}",
                detalle.Resumen.Id, resumen)
        };

        // El modelo ve solo un resumen corto; el borrador completo viaja aparte, en la respuesta.
        var datos = new JsonObject
        {
            ["ok"] = true,
            ["borradorListo"] = true,
            ["numero"] = detalle.Resumen.Numero,
            ["obra"] = obra,
            ["cantidadItems"] = items.Count
        };
        return new ResultadoHerramienta(true, datos, null, aprobacion);
    }

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

    private static JsonObject ResumenJson(RequisicionResumen r, IReadOnlyDictionary<Guid, string> obras) => new()
    {
        ["requisicionId"] = r.Id.ToString(),
        ["numero"] = r.Numero,
        ["estado"] = r.Estado,
        ["obra"] = obras.TryGetValue(r.ObraId, out var nombre) ? nombre : null,
        ["etapa"] = r.Etapa,
        ["fecha"] = r.Fecha.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        ["cantidadItems"] = r.CantidadItems
    };

    /// <summary>Recorta los bordes y cambia los caracteres de control por espacios. Null si queda vacío.</summary>
    private static string? LimpiarMotivo(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;
        var limpio = new string(texto.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        return limpio.Length == 0 ? null : limpio;
    }

    private static string? Acotar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;
        var limpio = texto.Trim();
        return limpio.Length <= MaxTextoLibre ? limpio : limpio[..MaxTextoLibre];
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

    /// <summary>Un número entero de JSON (7 o 7.0). Un texto ("7"), un decimal (2.5) o cualquier otra cosa da null.</summary>
    private static int? LeerEntero(JsonNode? nodo)
    {
        if (nodo is not JsonValue v)
            return null;
        if (v.TryGetValue<int>(out var entero))
            return entero;
        if (v.TryGetValue<double>(out var real) && real == Math.Floor(real) && real >= int.MinValue && real <= int.MaxValue)
            return (int)real;
        return null;
    }

    private static string? LeerTexto(JsonNode? nodo) =>
        nodo is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
