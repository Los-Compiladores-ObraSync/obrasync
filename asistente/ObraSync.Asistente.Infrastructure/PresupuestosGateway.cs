using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ObraSync.Asistente.Application;

namespace ObraSync.Asistente.Infrastructure;

/// <summary>Consulta partidas presupuestarias mediante el Gateway con el JWT del pedido actual.</summary>
public sealed class PresupuestosGateway : IConsultorPresupuestos
{
    public const int MaxPartidas = 50;
    public const int MaxBytes = 64 * 1024;
    public static readonly TimeSpan TiempoApi = TimeSpan.FromSeconds(3);

    private static readonly JsonSerializerOptions Opciones = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly ConfiguracionApi _config;
    private readonly IContextoDePeticion _contexto;

    public PresupuestosGateway(HttpClient http, ConfiguracionApi config, IContextoDePeticion contexto)
    {
        _http = http;
        _config = config;
        _contexto = contexto;
    }

    public async Task<ResultadoPartidasPresupuestarias> ListarPartidasAsync(
        Guid obraId,
        string? etapa,
        CancellationToken ct)
    {
        var jwt = _contexto.Jwt;
        if (string.IsNullOrWhiteSpace(jwt))
            return ResultadoPartidasPresupuestarias.Falla("sin_permiso");

        var parametros = $"obraId={obraId:D}&pagina=1&tamano={MaxPartidas}";
        if (!string.IsNullOrWhiteSpace(etapa))
            parametros += $"&etapa={Uri.EscapeDataString(etapa)}";

        var url = $"{_config.BaseUrl.TrimEnd('/')}/api/presupuestos/partidas?{parametros}";
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TiempoApi);

        using var pedido = new HttpRequestMessage(HttpMethod.Get, url);
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        pedido.Headers.TryAddWithoutValidation("X-Correlation-ID", _contexto.CorrelationId);

        try
        {
            using var resp = await _http.SendAsync(pedido, HttpCompletionOption.ResponseHeadersRead, limite.Token);
            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return ResultadoPartidasPresupuestarias.Falla("sin_permiso");
            if (!resp.IsSuccessStatusCode)
                return ResultadoPartidasPresupuestarias.Falla("api_http_error");

            if (resp.Content.Headers.ContentLength is long longitud && longitud > MaxBytes)
                return ResultadoPartidasPresupuestarias.Falla("respuesta_demasiado_grande");

            var bytes = await LeerRespuestaAcotadaAsync(resp.Content, limite.Token);
            if (bytes is null)
                return ResultadoPartidasPresupuestarias.Falla("respuesta_demasiado_grande");

            int? total = null;
            if (resp.Headers.TryGetValues("X-Total-Count", out var valores) &&
                int.TryParse(valores.FirstOrDefault(), out var cantidad) && cantidad >= 0)
                total = cantidad;

            var partidas = JsonSerializer.Deserialize<List<PartidaPresupuestariaAsistente>>(bytes, Opciones);
            return partidas is null
                ? ResultadoPartidasPresupuestarias.Falla("respuesta_invalida")
                : ResultadoPartidasPresupuestarias.Bien(partidas, total);
        }
        catch (JsonException)
        {
            return ResultadoPartidasPresupuestarias.Falla("respuesta_invalida");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ResultadoPartidasPresupuestarias.Falla("api_timeout");
        }
        catch (HttpRequestException)
        {
            return ResultadoPartidasPresupuestarias.Falla("api_no_disponible");
        }
    }

    private static async Task<byte[]?> LeerRespuestaAcotadaAsync(HttpContent contenido, CancellationToken ct)
    {
        await using var stream = await contenido.ReadAsStreamAsync(ct);
        using var respuesta = new MemoryStream(MaxBytes);
        var bloque = new byte[8192];

        while (true)
        {
            var restantesIncluyendoByteDeControl = MaxBytes + 1 - (int)respuesta.Length;
            var cantidadSolicitada = Math.Min(bloque.Length, restantesIncluyendoByteDeControl);
            var leidos = await stream.ReadAsync(bloque.AsMemory(0, cantidadSolicitada), ct);
            if (leidos == 0)
                return respuesta.ToArray();

            respuesta.Write(bloque, 0, leidos);
            if (respuesta.Length > MaxBytes)
                return null;
        }
    }
}
