using System.Text.Json;
using System.Text.Json.Nodes;

namespace ObraSync.Asistente.Application;

/// <summary>
/// Herramientas del Capataz para recibir mercadería (tarjeta #147):
///  - listar_ordenes_pendientes: las órdenes de compra que esperan recepción. Solo lectura.
///  - preparar_recepcion: ARMA el borrador de una recepción TOTAL. No registra nada: el frontend llama a
///    POST /api/recepciones, con el JWT del Capataz, cuando él toca Confirmar.
/// Solo recepción total: lo parcial lo rechaza el backend hasta #90. Igual que las otras herramientas, validan
/// los argumentos ANTES de llamar a la API y devuelven errores estables. El remito NUNCA se inventa: lo da la persona.
/// </summary>
public sealed class HerramientasRecepcion : GrupoHerramientasDePerfil
{
    public const string ListarOrdenes = "listar_ordenes_pendientes";
    public const string PrepararRecepcion = "preparar_recepcion";

    public const int MaxOrdenes = 5;
    /// <summary>Al preparar se vuelve a leer la lista para encontrar la orden: se pide la página más grande que acepta la API.</summary>
    public const int MaxBusqueda = 50;
    public const int MaxItemsPorOrden = 10;
    public const int MaxTextoLibre = 200;
    public const int MaxRemito = 50;
    public const int MaxObservaciones = 500;

    public static readonly IReadOnlyList<string> Nombres = new[] { ListarOrdenes, PrepararRecepcion };

    private readonly IConsultorRecepciones _recepciones;
    private readonly IBuscadorObras _obras;

    public HerramientasRecepcion(IConsultorRecepciones recepciones, IBuscadorObras obras)
    {
        _recepciones = recepciones;
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
                ["name"] = ListarOrdenes,
                ["description"] =
                    "Lista las órdenes de compra que están esperando recepción en obra (máximo 5), con su obra, etapa y materiales. " +
                    "Si hayMas es true, hay más órdenes que las que devolvió: decile cuántas hay en total. Solo lectura.",
                ["parameters"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }
            }
        },
        new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = PrepararRecepcion,
                ["description"] =
                    "Arma el BORRADOR de una recepción TOTAL (llegó todo lo comprado y está conforme). No registra nada: la persona lo revisa y toca Confirmar. " +
                    "Se necesita el ordenCompraId que devolvió listar_ordenes_pendientes en ESTE mismo pedido y el número de remito que dio la persona.",
                ["parameters"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["ordenCompraId"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Identificador (GUID) de la orden de compra, tal como lo devolvió listar_ordenes_pendientes."
                        },
                        ["numeroRemito"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Número de remito que dio la persona (hasta 50 caracteres). No lo inventes: si no lo dijo, preguntaselo."
                        },
                        ["observaciones"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Opcional. Una observación de la persona (hasta 500 caracteres)."
                        }
                    },
                    ["required"] = new JsonArray("ordenCompraId", "numeroRemito")
                }
            }
        }
    };

    /// <summary>Ejecuta una herramienta pedida por el modelo.</summary>
    public override Task<ResultadoHerramienta> EjecutarAsync(string nombre, string argumentosJson, CancellationToken ct) =>
        nombre switch
        {
            ListarOrdenes => ListarAsync(argumentosJson, ct),
            PrepararRecepcion => PrepararAsync(argumentosJson, ct),
            _ => Task.FromResult(ResultadoHerramienta.Error("herramienta_desconocida"))
        };

    private async Task<ResultadoHerramienta> ListarAsync(string argumentosJson, CancellationToken ct)
    {
        if (!TryLeerArgumentos(argumentosJson, out _))
            return ResultadoHerramienta.Error("argumentos_invalidos");

        var resultado = await _recepciones.ListarPendientesAsync(MaxOrdenes, ct);
        if (resultado.Error is not null)
            return ResultadoHerramienta.Error(resultado.Error);

        var nombresDeObras = await NombresDeObrasAsync(ct);
        var lista = new JsonArray();
        var numero = 0;
        foreach (var o in resultado.Ordenes!.Take(MaxOrdenes))
        {
            numero++;
            var items = new JsonArray();
            foreach (var i in o.Items.Take(MaxItemsPorOrden))
                items.Add(new JsonObject
                {
                    ["descripcion"] = Acotar(i.Descripcion),
                    ["cantidad"] = i.CantidadEsperada,
                    ["unidadMedida"] = i.UnidadMedida
                });

            lista.Add(new JsonObject
            {
                // "opcion" es el número que ve la persona en la lista; ordenCompraId es solo para preparar_recepcion.
                ["opcion"] = numero,
                ["ordenCompraId"] = o.OrdenCompraId.ToString(),
                ["obra"] = nombresDeObras.TryGetValue(o.ObraId, out var nombre) ? nombre : null,
                ["etapa"] = o.Etapa,
                ["cantidadItems"] = o.Items.Count,
                ["items"] = items
            });
        }

        var total = resultado.Total ?? resultado.Ordenes!.Count;
        var datos = new JsonObject
        {
            ["ok"] = true,
            ["total"] = total,
            // El modelo no tiene que adivinar si falta alguna: se lo decimos con un dato.
            ["mostradas"] = lista.Count,
            ["hayMas"] = total > lista.Count,
            ["ordenes"] = lista
        };
        return ResultadoHerramienta.Bien(datos);
    }

    private async Task<ResultadoHerramienta> PrepararAsync(string argumentosJson, CancellationToken ct)
    {
        if (!TryLeerArgumentos(argumentosJson, out var args))
            return ResultadoHerramienta.Error("argumentos_invalidos");

        var textoId = LeerTexto(args["ordenCompraId"]);
        if (string.IsNullOrWhiteSpace(textoId) || !Guid.TryParse(textoId.Trim(), out var ordenId) || ordenId == Guid.Empty)
            return ResultadoHerramienta.Error("argumentos_invalidos");

        // El remito lo da la persona: sin remito (o con uno que la API rechazaría) no se arma nada.
        var remito = LeerTexto(args["numeroRemito"])?.Trim();
        if (string.IsNullOrEmpty(remito) || remito.Length > MaxRemito)
            return ResultadoHerramienta.Error("argumentos_invalidos");

        string? observaciones = null;
        var nodoObservaciones = args["observaciones"];
        if (nodoObservaciones is not null)
        {
            var texto = LeerTexto(nodoObservaciones);
            if (texto is null)
                return ResultadoHerramienta.Error("argumentos_invalidos");
            texto = texto.Trim();
            if (texto.Length > MaxObservaciones)
                return ResultadoHerramienta.Error("argumentos_invalidos");
            observaciones = texto.Length == 0 ? null : texto;
        }

        // Siempre se vuelve a leer la lista: lo que existe y está pendiente lo decide la API, no lo que recuerde el modelo.
        var resultado = await _recepciones.ListarPendientesAsync(MaxBusqueda, ct);
        if (resultado.Error is not null)
            return ResultadoHerramienta.Error(resultado.Error);

        var orden = resultado.Ordenes!.FirstOrDefault(o => o.OrdenCompraId == ordenId);
        if (orden is null)
            return ResultadoHerramienta.Error("orden_no_encontrada");
        if (orden.Items.Count == 0)
            return ResultadoHerramienta.Error("orden_sin_items");

        var nombresDeObras = await NombresDeObrasAsync(ct);
        nombresDeObras.TryGetValue(orden.ObraId, out var obra);

        // Recepción TOTAL: llegó lo comprado y está conforme (igual que la pantalla de Recepción).
        var detalles = orden.Items
            .Select(i => new BorradorDetalleRecepcion(i.ArticuloId, i.Descripcion, i.CantidadEsperada, i.CantidadEsperada, i.UnidadMedida, true))
            .ToList();
        var resumenItems = orden.Items
            .Select(i => new RecepcionResumenItem(Acotar(i.Descripcion) ?? "", i.CantidadEsperada, i.UnidadMedida))
            .ToList();

        var borrador = new BorradorRecepcion(
            BorradorRecepcion.AccionRegistrarRecepcion,
            $"Registrar recepción · remito {remito}",
            new RecepcionResumen(obra, orden.Etapa, remito, resumenItems),
            new BorradorCrearRecepcion(orden.ObraId, orden.OrdenCompraId, remito, observaciones, detalles));

        // El modelo ve solo un resumen corto; el borrador completo viaja aparte, en la respuesta.
        var datos = new JsonObject
        {
            ["ok"] = true,
            ["borradorListo"] = true,
            ["numeroRemito"] = remito,
            ["obra"] = obra,
            ["cantidadItems"] = detalles.Count
        };
        return new ResultadoHerramienta(true, datos, null, null, borrador);
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

    private static string? LeerTexto(JsonNode? nodo) =>
        nodo is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
