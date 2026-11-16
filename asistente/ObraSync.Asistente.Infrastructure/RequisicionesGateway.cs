using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ObraSync.Asistente.Application;

namespace ObraSync.Asistente.Infrastructure;

/// <summary>
/// Lee las requisiciones REALES a través del Gateway, con el JWT del usuario que escribe (nunca una cuenta propia).
/// Solo lee (GET): la API ya filtra por rol, así que cada usuario ve únicamente lo que podría ver en la aplicación.
/// Aplica los límites del harness: timeout corto, tamaño máximo y errores estables.
/// </summary>
public sealed class RequisicionesGateway : IConsultorRequisiciones
{
    public const int MaxBytes = 64 * 1024;
    public static readonly TimeSpan TiempoApi = TimeSpan.FromSeconds(3);

    private static readonly JsonSerializerOptions Opciones = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly ConfiguracionApi _config;
    private readonly IContextoDePeticion _contexto;

    public RequisicionesGateway(HttpClient http, ConfiguracionApi config, IContextoDePeticion contexto)
    {
        _http = http;
        _config = config;
        _contexto = contexto;
    }

    // Lo que devuelve la API (RequisicionResponseDto). Los campos que no declaramos acá se descartan solos.
    private sealed record ItemDto(string? Descripcion, decimal Cantidad, string? UnidadMedida);
    private sealed record RequisicionDto(
        Guid RequisicionId, string? NumeroRequisicion, Guid ObraId, string? Etapa, string? Estado,
        string? Observaciones, string? MotivoRechazo, DateTime FechaCreacion, List<ItemDto>? Items);

    public async Task<ResultadoListaRequisiciones> ListarAsync(string? estado, int maximo, CancellationToken ct)
    {
        var url = $"{_config.BaseUrl}/api/requisiciones?pagina=1&tamano={maximo}";
        if (!string.IsNullOrWhiteSpace(estado))
            url += $"&estado={Uri.EscapeDataString(estado)}";

        var (respuesta, error) = await LeerAsync(url, ct);
        if (error is not null)
            return ResultadoListaRequisiciones.Falla(error);

        try
        {
            var dtos = JsonSerializer.Deserialize<List<RequisicionDto>>(respuesta!.Bytes, Opciones);
            if (dtos is null)
                return ResultadoListaRequisiciones.Falla("respuesta_invalida");

            var lista = dtos.Take(maximo).Select(Resumen).ToList();
            return ResultadoListaRequisiciones.Bien(lista, respuesta.Total);
        }
        catch (JsonException)
        {
            return ResultadoListaRequisiciones.Falla("respuesta_invalida");
        }
    }

    public async Task<ResultadoRequisicion> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var url = $"{_config.BaseUrl}/api/requisiciones/{id}";

        var (respuesta, error) = await LeerAsync(url, ct);
        if (error is not null)
            return ResultadoRequisicion.Falla(error);

        try
        {
            var dto = JsonSerializer.Deserialize<RequisicionDto>(respuesta!.Bytes, Opciones);
            if (dto is null)
                return ResultadoRequisicion.Falla("respuesta_invalida");

            var items = (dto.Items ?? new List<ItemDto>())
                .Select(i => new ItemRequisicion(i.Descripcion ?? "", i.Cantidad, i.UnidadMedida ?? ""))
                .ToList();
            return ResultadoRequisicion.Bien(new RequisicionDetalle(Resumen(dto), dto.Observaciones, dto.MotivoRechazo, items));
        }
        catch (JsonException)
        {
            return ResultadoRequisicion.Falla("respuesta_invalida");
        }
    }

    private static RequisicionResumen Resumen(RequisicionDto d) => new(
        d.RequisicionId, d.NumeroRequisicion ?? "", d.Estado ?? "", d.ObraId, d.Etapa, d.FechaCreacion, d.Items?.Count ?? 0);

    private sealed record Leida(byte[] Bytes, int? Total);

    /// <summary>Hace el GET con el JWT del usuario y devuelve el cuerpo o un código de error estable.</summary>
    private async Task<(Leida? Respuesta, string? Error)> LeerAsync(string url, CancellationToken ct)
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
                return (null, "requisicion_no_encontrada");
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
