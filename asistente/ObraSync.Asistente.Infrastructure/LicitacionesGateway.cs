using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ObraSync.Asistente.Application;

namespace ObraSync.Asistente.Infrastructure;

/// <summary>
/// Lee las licitaciones y cotizaciones REALES a través del Gateway, con el JWT del usuario que escribe (nunca una cuenta propia).
/// Solo lee (GET): la API ya filtra por rol, así que cada usuario ve únicamente lo que podría ver en la aplicación.
/// Aplica los límites del harness: timeout corto, tamaño máximo y errores estables.
/// </summary>
public sealed class LicitacionesGateway : IConsultorLicitaciones
{
    public const int MaxBytes = 64 * 1024;
    public static readonly TimeSpan TiempoApi = TimeSpan.FromSeconds(3);

    private static readonly JsonSerializerOptions Opciones = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly ConfiguracionApi _config;
    private readonly IContextoDePeticion _contexto;

    public LicitacionesGateway(HttpClient http, ConfiguracionApi config, IContextoDePeticion contexto)
    {
        _http = http;
        _config = config;
        _contexto = contexto;
    }

    // Lo que devuelve la API (LicitacionResponseDto y CotizacionResponseDto). Lo que no declaramos acá se descarta solo.
    private sealed record ItemPliegoDto(string? Descripcion, decimal Cantidad, string? UnidadMedida);
    private sealed record LicitacionDto(
        Guid LicitacionId, Guid RequisicionId, string? NumeroRequisicion, Guid ObraId, string? Estado,
        DateTime FechaLimiteCotizacion, List<ItemPliegoDto>? Items);
    private sealed record CotizacionDto(
        Guid CotizacionId, Guid ProveedorId, string? ProveedorNombre, decimal SubtotalItems, decimal Flete,
        decimal CostoTotal, int? PlazoEntregaDias, decimal PuntajeTotal, bool EsGanadora, bool EsMasBarata,
        decimal? CalificacionProveedor);

    public async Task<ResultadoListaLicitaciones> ListarAsync(string? estado, int maximo, CancellationToken ct)
    {
        var url = $"{_config.BaseUrl}/api/licitaciones?pagina=1&tamano={maximo}";
        if (!string.IsNullOrWhiteSpace(estado))
            url += $"&estado={Uri.EscapeDataString(estado)}";

        var (respuesta, error) = await LeerAsync(url, "licitacion_no_encontrada", ct);
        if (error is not null)
            return ResultadoListaLicitaciones.Falla(error);

        try
        {
            var dtos = JsonSerializer.Deserialize<List<LicitacionDto>>(respuesta!.Bytes, Opciones);
            if (dtos is null)
                return ResultadoListaLicitaciones.Falla("respuesta_invalida");

            return ResultadoListaLicitaciones.Bien(dtos.Take(maximo).Select(Resumen).ToList(), respuesta.Total);
        }
        catch (JsonException)
        {
            return ResultadoListaLicitaciones.Falla("respuesta_invalida");
        }
    }

    public async Task<ResultadoLicitacion> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var url = $"{_config.BaseUrl}/api/licitaciones/{id}";

        var (respuesta, error) = await LeerAsync(url, "licitacion_no_encontrada", ct);
        if (error is not null)
            return ResultadoLicitacion.Falla(error);

        try
        {
            var dto = JsonSerializer.Deserialize<LicitacionDto>(respuesta!.Bytes, Opciones);
            if (dto is null)
                return ResultadoLicitacion.Falla("respuesta_invalida");

            var items = (dto.Items ?? new List<ItemPliegoDto>())
                .Select(i => new ItemPliego(i.Descripcion ?? "", i.Cantidad, i.UnidadMedida ?? ""))
                .ToList();
            return ResultadoLicitacion.Bien(new LicitacionDetalle(Resumen(dto), items));
        }
        catch (JsonException)
        {
            return ResultadoLicitacion.Falla("respuesta_invalida");
        }
    }

    public async Task<ResultadoCotizaciones> ListarCotizacionesAsync(Guid licitacionId, int maximo, CancellationToken ct)
    {
        var url = $"{_config.BaseUrl}/api/cotizaciones?licitacionId={licitacionId}&pagina=1&tamano={maximo}";

        var (respuesta, error) = await LeerAsync(url, "licitacion_no_encontrada", ct);
        if (error is not null)
            return ResultadoCotizaciones.Falla(error);

        try
        {
            var dtos = JsonSerializer.Deserialize<List<CotizacionDto>>(respuesta!.Bytes, Opciones);
            if (dtos is null)
                return ResultadoCotizaciones.Falla("respuesta_invalida");

            var lista = dtos.Take(maximo).Select(d => new CotizacionResumen(
                d.CotizacionId, d.ProveedorId, d.ProveedorNombre, d.SubtotalItems, d.Flete, d.CostoTotal,
                d.PlazoEntregaDias, d.PuntajeTotal, d.EsGanadora, d.EsMasBarata, d.CalificacionProveedor)).ToList();
            return ResultadoCotizaciones.Bien(lista, respuesta.Total);
        }
        catch (JsonException)
        {
            return ResultadoCotizaciones.Falla("respuesta_invalida");
        }
    }

    private static LicitacionResumen Resumen(LicitacionDto d) => new(
        d.LicitacionId, d.RequisicionId, d.NumeroRequisicion, d.ObraId, d.Estado ?? "",
        d.FechaLimiteCotizacion, d.Items?.Count ?? 0);

    private sealed record Leida(byte[] Bytes, int? Total);

    /// <summary>Hace el GET con el JWT del usuario y devuelve el cuerpo o un código de error estable.</summary>
    private async Task<(Leida? Respuesta, string? Error)> LeerAsync(string url, string errorNoEncontrado, CancellationToken ct)
    {
        var jwt = _contexto.Jwt;
        if (string.IsNullOrWhiteSpace(jwt))
            return (null, "sin_permiso");

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
                return (null, "sin_permiso");
            if (resp.StatusCode == HttpStatusCode.NotFound)
                return (null, errorNoEncontrado);
            if (!resp.IsSuccessStatusCode)
                return (null, "api_http_error");

            var bytes = await resp.Content.ReadAsByteArrayAsync(limite.Token);
            if (bytes.Length > MaxBytes)
                return (null, "respuesta_demasiado_grande");

            int? total = null;
            if (resp.Headers.TryGetValues("X-Total-Count", out var valores) &&
                int.TryParse(valores.FirstOrDefault(), out var n) && n >= 0)
                total = n;

            return (new Leida(bytes, total), null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (null, "api_timeout");
        }
        catch (HttpRequestException)
        {
            return (null, "api_no_disponible");
        }
    }
}
