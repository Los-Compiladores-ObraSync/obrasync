using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using ObraSync.Asistente.Application;

namespace ObraSync.Asistente.Tests;

/// <summary>Modelo falso: devuelve respuestas preparadas, sin gastar cupo de Azure. Anota lo que recibió.</summary>
internal sealed class ModeloFalso : IModeloChat
{
    private readonly Queue<Func<JsonArray, RespuestaModelo>> _pasos;

    /// <summary>Copia de los mensajes que recibió en cada llamada (para revisar qué se le mandó).</summary>
    public List<JsonArray> Pedidos { get; } = new();

    public ModeloFalso(params Func<JsonArray, RespuestaModelo>[] pasos) => _pasos = new(pasos);

    public Task<RespuestaModelo> PedirAsync(JsonArray mensajes, JsonArray? herramientas, int maxTokensSalida, CancellationToken ct)
    {
        Pedidos.Add((JsonArray)mensajes.DeepClone());
        return Task.FromResult(_pasos.Dequeue()(mensajes));
    }
}

/// <summary>Respuestas armadas del modelo: texto final o pedido de herramienta.</summary>
internal static class Respuestas
{
    public static RespuestaModelo Final(string texto, int tokens = 10) =>
        new(new JsonObject { ["role"] = "assistant", ["content"] = texto }, "stop", tokens);

    public static RespuestaModelo PideHerramienta(string argumentos, int tokens = 10, string nombre = "buscar_insumos") =>
        new(new JsonObject
        {
            ["role"] = "assistant",
            ["content"] = null,
            ["tool_calls"] = new JsonArray(new JsonObject
            {
                ["id"] = "call_1",
                ["type"] = "function",
                ["function"] = new JsonObject { ["name"] = nombre, ["arguments"] = argumentos }
            })
        }, "tool_calls", tokens);
}

/// <summary>Catálogo falso en memoria.</summary>
internal sealed class BuscadorFalso : IBuscadorInsumos
{
    private static readonly Insumo[] Catalogo =
    {
        new("00000000-0000-0000-0000-000000000001", "MAT-CEM-001", "Cemento Portland 50 kg", "BOLSA"),
        new("00000000-0000-0000-0000-000000000002", "MAT-ARE-001", "Arena gruesa", "M3"),
        new("00000000-0000-0000-0000-000000000003", "MAT-HIE-008", "Hierro del 8", "UNIDAD"),
    };

    public Task<ResultadoBusqueda> BuscarAsync(string texto, CancellationToken ct)
    {
        var r = Catalogo.Where(i => i.Descripcion.Contains(texto.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        return Task.FromResult(ResultadoBusqueda.Bien(r));
    }
}

internal sealed class ContextoFalso : IContextoDePeticion
{
    public string? Jwt { get; set; } = "JWT_DE_PRUEBA";
    /// <summary>Por defecto Capataz, como la primera versión del asistente.</summary>
    public string? Rol { get; set; } = "Capataz";
    public string CorrelationId { get; set; } = "11111111-2222-3333-4444-555555555555";
}

/// <summary>Hace de gateway de IA: anota el pedido y responde con el código pedido.</summary>
internal sealed class HandlerModelo : HttpMessageHandler
{
    private readonly int _estado;
    public string? ApiKey { get; private set; }
    public string? Url { get; private set; }
    public string? Cuerpo { get; private set; }
    public int Llamadas { get; private set; }

    public HandlerModelo(int estado = 200) => _estado = estado;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        Llamadas++;
        ApiKey = req.Headers.GetValues("api-key").FirstOrDefault();
        Url = req.RequestUri!.AbsoluteUri;
        Cuerpo = req.Content is null ? null : await req.Content.ReadAsStringAsync(ct);
        const string ok = "{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"role\":\"assistant\",\"content\":\"¡Hola!\"}}],\"usage\":{\"total_tokens\":16}}";
        return new HttpResponseMessage((HttpStatusCode)_estado)
        {
            Content = new StringContent(_estado == 200 ? ok : "{}", Encoding.UTF8, "application/json")
        };
    }
}

/// <summary>Hace de API Gateway de ObraSync (catálogo): anota el pedido; puede demorar o caerse.</summary>
internal sealed class HandlerCatalogo : HttpMessageHandler
{
    private readonly int _estado;
    private readonly string _cuerpo;
    private readonly bool _lanza;
    private readonly TimeSpan _demora;

    public string? Authorization { get; private set; }
    public string? CorrelationId { get; private set; }
    public string? Url { get; private set; }

    public HandlerCatalogo(int estado, string cuerpo, bool lanzaError = false, TimeSpan demora = default)
    {
        _estado = estado;
        _cuerpo = cuerpo;
        _lanza = lanzaError;
        _demora = demora;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        Authorization = req.Headers.Authorization?.ToString();
        CorrelationId = req.Headers.TryGetValues("X-Correlation-ID", out var v) ? v.FirstOrDefault() : null;
        Url = req.RequestUri!.AbsoluteUri;
        if (_lanza) throw new HttpRequestException("caído");
        if (_demora > TimeSpan.Zero) await Task.Delay(_demora, ct);
        return new HttpResponseMessage((HttpStatusCode)_estado)
        {
            Content = new StringContent(_cuerpo, Encoding.UTF8, "application/json")
        };
    }
}

/// <summary>Obras falsas en memoria (como las vería un Capataz). Se puede cambiar el resultado y contar las llamadas.</summary>
internal sealed class ObrasFalsas : IBuscadorObras
{
    public static readonly Guid IdTorre = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly Guid IdGalpon = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    public ResultadoObras Resultado { get; set; } = ResultadoObras.Bien(new[]
    {
        new ObraAsistente(IdTorre, "Torre Central", new[] { "Estructura", "Obra Gruesa" }),
        new ObraAsistente(IdGalpon, "Galpón Norte", new string[0])
    });

    public int Llamadas { get; private set; }

    public Task<ResultadoObras> ListarAsync(CancellationToken ct)
    {
        Llamadas++;
        return Task.FromResult(Resultado);
    }
}

/// <summary>
/// Hace de Gateway con varias rutas (obras y catálogo) y anota TODOS los pedidos que recibe:
/// sirve para probar que el asistente solo hace GET y que siempre manda el JWT del usuario.
/// </summary>
internal sealed class HandlerRutas : HttpMessageHandler
{
    private readonly Dictionary<string, string> _respuestas;

    public List<(HttpMethod Metodo, string Url, string? Authorization)> Pedidos { get; } = new();

    public HandlerRutas(Dictionary<string, string> respuestasPorRuta) => _respuestas = respuestasPorRuta;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        Pedidos.Add((req.Method, req.RequestUri!.AbsoluteUri, req.Headers.Authorization?.ToString()));
        var ruta = req.RequestUri!.AbsolutePath;
        var cuerpo = _respuestas.FirstOrDefault(r => ruta.StartsWith(r.Key, StringComparison.OrdinalIgnoreCase)).Value;
        return Task.FromResult(cuerpo is null
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") });
    }
}


/// <summary>Requisiciones falsas en memoria (como las vería un Comprador). Se puede cambiar el resultado y contar las llamadas.</summary>
internal sealed class ConsultorFalso : IConsultorRequisiciones
{
    public static readonly Guid IdPendiente = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    public static readonly Guid IdAprobada = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    public static RequisicionResumen Pendiente { get; } =
        new(IdPendiente, "REQ-0001", "Pendiente", ObrasFalsas.IdTorre, "Estructura", new DateTime(2026, 11, 10), 2);

    public static RequisicionResumen Aprobada { get; } =
        new(IdAprobada, "REQ-0002", "Aprobada", ObrasFalsas.IdGalpon, null, new DateTime(2026, 11, 9), 1);

    public ResultadoListaRequisiciones ResultadoLista { get; set; } =
        ResultadoListaRequisiciones.Bien(new[] { Pendiente, Aprobada }, 2);

    public ResultadoRequisicion ResultadoDetalle { get; set; } = ResultadoRequisicion.Bien(new RequisicionDetalle(
        Pendiente, "Urge para el colado", null,
        new[] { new ItemRequisicion("Cemento Portland 50 kg", 20, "BOLSA"), new ItemRequisicion("Arena gruesa", 3, "M3") }));

    public int LlamadasLista { get; private set; }
    public int LlamadasDetalle { get; private set; }
    public string? UltimoEstado { get; private set; }
    public int UltimoMaximo { get; private set; }
    public Guid? UltimoId { get; private set; }

    public Task<ResultadoListaRequisiciones> ListarAsync(string? estado, int maximo, CancellationToken ct)
    {
        LlamadasLista++;
        UltimoEstado = estado;
        UltimoMaximo = maximo;
        return Task.FromResult(ResultadoLista);
    }

    public Task<ResultadoRequisicion> ObtenerAsync(Guid id, CancellationToken ct)
    {
        LlamadasDetalle++;
        UltimoId = id;
        return Task.FromResult(ResultadoDetalle);
    }
}


/// <summary>Órdenes pendientes de recepción falsas en memoria. Se puede cambiar el resultado y contar las llamadas.</summary>
internal sealed class ConsultorRecepcionesFalso : IConsultorRecepciones
{
    public static readonly Guid IdOrden = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    public static readonly Guid IdArticulo1 = Guid.Parse("dddddddd-0000-0000-0000-000000000001");
    public static readonly Guid IdArticulo2 = Guid.Parse("dddddddd-0000-0000-0000-000000000002");

    public static OrdenPendiente Orden { get; } = new(IdOrden, ObrasFalsas.IdTorre, "Estructura", new[]
    {
        new ItemOrdenPendiente(IdArticulo1, "Cemento Portland 50 kg", 80, "BOLSA"),
        new ItemOrdenPendiente(IdArticulo2, "Arena gruesa", 12, "M3")
    });

    public ResultadoOrdenesPendientes Resultado { get; set; } = ResultadoOrdenesPendientes.Bien(new[] { Orden }, 1);

    public int Llamadas { get; private set; }
    public int UltimoMaximo { get; private set; }

    public Task<ResultadoOrdenesPendientes> ListarPendientesAsync(int maximo, CancellationToken ct)
    {
        Llamadas++;
        UltimoMaximo = maximo;
        return Task.FromResult(Resultado);
    }
}
