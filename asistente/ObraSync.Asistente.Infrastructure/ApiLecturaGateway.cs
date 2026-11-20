using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using ObraSync.Asistente.Application;
using ObraSync.Asistente.Application.Generadas;

namespace ObraSync.Asistente.Infrastructure;

/// <summary>
/// Cliente de lectura genérico para las herramientas generadas desde el contrato (#151).
/// Mismos límites que los gateways escritos a mano: JWT del usuario, X-Correlation-ID, timeout corto,
/// tamaño máximo y errores estables. Solo hace GET y solo a rutas /api/ del Gateway configurado.
/// </summary>
public sealed class ApiLecturaGateway : IClienteApiLectura
{
    /// <summary>
    /// Tope de la respuesta. 12 KB son unos 3.000 tokens: el cupo del grupo es de 10.000 tokens por MINUTO (#144),
    /// así que una respuesta de 32 KB podía gastarlo casi entero y pausar el asistente. Si no entra, el modelo recibe
    /// respuesta_demasiado_grande y se lo dice a la persona (que puede pedir menos resultados).
    /// </summary>
    public const int MaxBytes = 12 * 1024;
    public static readonly TimeSpan TiempoApi = TimeSpan.FromSeconds(3);

    private readonly HttpClient _http;
    private readonly ConfiguracionApi _config;
    private readonly IContextoDePeticion _contexto;

    public ApiLecturaGateway(HttpClient http, ConfiguracionApi config, IContextoDePeticion contexto)
    {
        _http = http;
        _config = config;
        _contexto = contexto;
    }

    public async Task<RespuestaApiLectura> GetAsync(string rutaYConsulta, CancellationToken ct)
    {
        // Defensa extra: la ruta la arma HerramientasGeneradas, pero acá se revisa que no se pueda salir de /api/.
        if (!rutaYConsulta.StartsWith("/api/", StringComparison.Ordinal) || rutaYConsulta.Contains("//") || rutaYConsulta.Contains(".."))
            return RespuestaApiLectura.Falla("ruta_invalida");

        var jwt = _contexto.Jwt;
        if (string.IsNullOrWhiteSpace(jwt))
            return RespuestaApiLectura.Falla("sin_permiso");

        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TiempoApi);

        using var pedido = new HttpRequestMessage(HttpMethod.Get, _config.BaseUrl + rutaYConsulta);
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        pedido.Headers.TryAddWithoutValidation("X-Correlation-ID", _contexto.CorrelationId);

        try
        {
            using var resp = await _http.SendAsync(pedido, HttpCompletionOption.ResponseHeadersRead, limite.Token);

            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return RespuestaApiLectura.Falla("sin_permiso");
            if (resp.StatusCode == HttpStatusCode.NotFound)
                return RespuestaApiLectura.Falla("no_encontrado");
            if (!resp.IsSuccessStatusCode)
                return RespuestaApiLectura.Falla("api_http_error");

            var bytes = await resp.Content.ReadAsByteArrayAsync(limite.Token);
            if (bytes.Length > MaxBytes)
                return RespuestaApiLectura.Falla("respuesta_demasiado_grande");

            int? total = null;
            if (resp.Headers.TryGetValues("X-Total-Count", out var valores) &&
                int.TryParse(valores.FirstOrDefault(), out var n) && n >= 0)
                total = n;

            try
            {
                var cuerpo = JsonNode.Parse(bytes);
                return cuerpo is null ? RespuestaApiLectura.Falla("respuesta_invalida") : RespuestaApiLectura.Bien(cuerpo, total);
            }
            catch (JsonException)
            {
                return RespuestaApiLectura.Falla("respuesta_invalida");
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return RespuestaApiLectura.Falla("api_timeout");
        }
        catch (HttpRequestException)
        {
            return RespuestaApiLectura.Falla("api_no_disponible");
        }
    }
}
