using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using ObraSync.Asistente.Application;

namespace ObraSync.Asistente.Infrastructure;

/// <summary>Dónde está el API Gateway de ObraSync (por él se consulta el catálogo).</summary>
public sealed class ConfiguracionApi
{
    public string BaseUrl { get; }

    public ConfiguracionApi(string? baseUrl)
    {
        BaseUrl = (string.IsNullOrWhiteSpace(baseUrl) ? "http://localhost:5000" : baseUrl).Trim().TrimEnd('/');
    }

    /// <summary>Lee Gateway:BaseUrl (en Docker: Gateway__BaseUrl, que apunta a http://gateway:8080).</summary>
    public static ConfiguracionApi Desde(IConfiguration configuracion) => new(configuracion["Gateway:BaseUrl"]);
}

/// <summary>
/// Busca en el catálogo REAL a través del Gateway, con el JWT del usuario que escribe (nunca una cuenta propia).
/// Aplica los límites del harness: timeout corto, tamaño máximo, pocos resultados y errores estables.
/// </summary>
public sealed class BuscadorGateway : IBuscadorInsumos
{
    public const int MaxBytes = 32 * 1024;      // si la API devuelve más, no se lo pasamos al modelo
    public const int MaxResultados = 5;         // el modelo no necesita ver 200 insumos: se los pedimos ya paginados (#172)
    public static readonly TimeSpan TiempoApi = TimeSpan.FromSeconds(3);

    private static readonly JsonSerializerOptions Opciones = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly ConfiguracionApi _config;
    private readonly IContextoDePeticion _contexto;

    public BuscadorGateway(HttpClient http, ConfiguracionApi config, IContextoDePeticion contexto)
    {
        _http = http;
        _config = config;
        _contexto = contexto;
    }

    // Lo que devuelve el API (InsumoCatalogoDto). Los campos que no declaramos acá se descartan solos.
    private sealed record InsumoDto(Guid ArticuloId, string? Codigo, string? Descripcion, string? UnidadMedida);

    public async Task<ResultadoBusqueda> BuscarAsync(string texto, CancellationToken ct)
    {
        var jwt = _contexto.Jwt;
        if (string.IsNullOrWhiteSpace(jwt))
            return ResultadoBusqueda.Falla("sin_permiso");

        var url = $"{_config.BaseUrl}/api/catalogo/insumos?search={Uri.EscapeDataString(texto.Trim())}&soloActivos=true&pagina=1&tamano={MaxResultados}";

        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TiempoApi);

        using var pedido = new HttpRequestMessage(HttpMethod.Get, url);
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        // El Gateway propaga este encabezado a todos los servicios para rastrear el pedido.
        pedido.Headers.TryAddWithoutValidation("X-Correlation-ID", _contexto.CorrelationId);

        try
        {
            using var resp = await _http.SendAsync(pedido, HttpCompletionOption.ResponseHeadersRead, limite.Token);

            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return ResultadoBusqueda.Falla("sin_permiso");
            if (!resp.IsSuccessStatusCode)
                return ResultadoBusqueda.Falla("api_http_error");

            var bytes = await resp.Content.ReadAsByteArrayAsync(limite.Token);
            if (bytes.Length > MaxBytes)
                return ResultadoBusqueda.Falla("respuesta_demasiado_grande");

            var dtos = JsonSerializer.Deserialize<List<InsumoDto>>(bytes, Opciones);
            if (dtos is null)
                return ResultadoBusqueda.Falla("respuesta_invalida");

            var insumos = dtos
                .Where(d => !string.IsNullOrWhiteSpace(d.Codigo) && !string.IsNullOrWhiteSpace(d.Descripcion))
                .Take(MaxResultados)
                .Select(d => new Insumo(d.ArticuloId.ToString(), d.Codigo!, d.Descripcion!, d.UnidadMedida ?? ""))
                .ToList();
            return ResultadoBusqueda.Bien(insumos);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ResultadoBusqueda.Falla("api_timeout");
        }
        catch (HttpRequestException)
        {
            return ResultadoBusqueda.Falla("api_no_disponible");
        }
        catch (JsonException)
        {
            return ResultadoBusqueda.Falla("respuesta_invalida");
        }
    }
}
