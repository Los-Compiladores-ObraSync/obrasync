using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ObraSync.Asistente.Application;

namespace ObraSync.Asistente.Infrastructure;

/// <summary>
/// Lista las obras REALES a través del Gateway, con el JWT del usuario (nunca una cuenta propia).
/// Solo lee (GET): el asistente no tiene ninguna forma de escribir.
/// </summary>
public sealed class ObrasGateway : IBuscadorObras
{
    public const int MaxBytes = 64 * 1024;
    public const int MaxObras = 50;
    public static readonly TimeSpan TiempoApi = TimeSpan.FromSeconds(3);

    private static readonly JsonSerializerOptions Opciones = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly ConfiguracionApi _config;
    private readonly IContextoDePeticion _contexto;

    public ObrasGateway(HttpClient http, ConfiguracionApi config, IContextoDePeticion contexto)
    {
        _http = http;
        _config = config;
        _contexto = contexto;
    }

    private sealed record EtapaDto(string? NombreEtapa);
    private sealed record ObraDto(Guid Id, string? Nombre, bool? Activa, List<EtapaDto>? Etapas);

    public async Task<ResultadoObras> ListarAsync(CancellationToken ct)
    {
        var jwt = _contexto.Jwt;
        if (string.IsNullOrWhiteSpace(jwt))
            return ResultadoObras.Falla("sin_permiso");

        // pagina/tamano: paginación de #172. Si el servicio todavía no la soporta, ignora los parámetros.
        var url = $"{_config.BaseUrl}/api/obras?pagina=1&tamano={MaxObras}";

        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TiempoApi);

        using var pedido = new HttpRequestMessage(HttpMethod.Get, url);
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        pedido.Headers.TryAddWithoutValidation("X-Correlation-ID", _contexto.CorrelationId);

        try
        {
            using var resp = await _http.SendAsync(pedido, HttpCompletionOption.ResponseHeadersRead, limite.Token);

            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return ResultadoObras.Falla("sin_permiso");
            if (!resp.IsSuccessStatusCode)
                return ResultadoObras.Falla("api_http_error");

            var bytes = await resp.Content.ReadAsByteArrayAsync(limite.Token);
            if (bytes.Length > MaxBytes)
                return ResultadoObras.Falla("respuesta_demasiado_grande");

            var dtos = JsonSerializer.Deserialize<List<ObraDto>>(bytes, Opciones);
            if (dtos is null)
                return ResultadoObras.Falla("respuesta_invalida");

            var obras = dtos
                .Where(o => o.Activa != false && !string.IsNullOrWhiteSpace(o.Nombre))
                .Take(MaxObras)
                .Select(o => new ObraAsistente(
                    o.Id,
                    o.Nombre!,
                    (o.Etapas ?? new List<EtapaDto>())
                        .Where(e => !string.IsNullOrWhiteSpace(e.NombreEtapa))
                        .Select(e => e.NombreEtapa!)
                        .ToList()))
                .ToList();
            return ResultadoObras.Bien(obras);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ResultadoObras.Falla("api_timeout");
        }
        catch (HttpRequestException)
        {
            return ResultadoObras.Falla("api_no_disponible");
        }
        catch (JsonException)
        {
            return ResultadoObras.Falla("respuesta_invalida");
        }
    }
}
