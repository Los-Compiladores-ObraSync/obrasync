using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ObraSync.Asistente.Application;

namespace ObraSync.Asistente.Infrastructure;

/// <summary>
/// Lee las órdenes de compra pendientes de recepción a través del Gateway, con el JWT del usuario que escribe
/// (nunca una cuenta propia). Solo hace GET: registrar la recepción lo hace el frontend cuando el usuario confirma.
/// Aplica los mismos límites que los otros lectores: timeout corto, tamaño máximo y errores estables.
/// </summary>
public sealed class RecepcionesGateway : IConsultorRecepciones
{
    public const int MaxBytes = 64 * 1024;
    public static readonly TimeSpan TiempoApi = TimeSpan.FromSeconds(3);

    private static readonly JsonSerializerOptions Opciones = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly ConfiguracionApi _config;
    private readonly IContextoDePeticion _contexto;

    public RecepcionesGateway(HttpClient http, ConfiguracionApi config, IContextoDePeticion contexto)
    {
        _http = http;
        _config = config;
        _contexto = contexto;
    }

    // Lo que devuelve la API (OrdenPendienteDto). Los campos que no declaramos acá se descartan solos.
    private sealed record ItemDto(Guid ArticuloId, string? DescripcionArticulo, decimal CantidadEsperada, string? UnidadMedida);
    private sealed record OrdenDto(Guid OrdenCompraId, Guid ObraId, string? Etapa, List<ItemDto>? Items);

    public async Task<ResultadoOrdenesPendientes> ListarPendientesAsync(int maximo, CancellationToken ct)
    {
        var jwt = _contexto.Jwt;
        if (string.IsNullOrWhiteSpace(jwt))
            return ResultadoOrdenesPendientes.Falla("sin_permiso");

        var url = $"{_config.BaseUrl}/api/recepciones/ordenes-pendientes?pagina=1&tamano={maximo}";

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
                return ResultadoOrdenesPendientes.Falla("sin_permiso");
            if (!resp.IsSuccessStatusCode)
                return ResultadoOrdenesPendientes.Falla("api_http_error");

            var bytes = await resp.Content.ReadAsByteArrayAsync(limite.Token);
            if (bytes.Length > MaxBytes)
                return ResultadoOrdenesPendientes.Falla("respuesta_demasiado_grande");

            int? total = null;
            if (resp.Headers.TryGetValues("X-Total-Count", out var valores) &&
                int.TryParse(valores.FirstOrDefault(), out var n) && n >= 0)
                total = n;

            var dtos = JsonSerializer.Deserialize<List<OrdenDto>>(bytes, Opciones);
            if (dtos is null)
                return ResultadoOrdenesPendientes.Falla("respuesta_invalida");

            var ordenes = dtos
                .Take(maximo)
                .Select(o => new OrdenPendiente(
                    o.OrdenCompraId, o.ObraId, o.Etapa,
                    (o.Items ?? new List<ItemDto>())
                        .Select(i => new ItemOrdenPendiente(i.ArticuloId, i.DescripcionArticulo ?? "", i.CantidadEsperada, i.UnidadMedida ?? ""))
                        .ToList()))
                .ToList();
            return ResultadoOrdenesPendientes.Bien(ordenes, total);
        }
        catch (JsonException)
        {
            return ResultadoOrdenesPendientes.Falla("respuesta_invalida");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ResultadoOrdenesPendientes.Falla("api_timeout");
        }
        catch (HttpRequestException)
        {
            return ResultadoOrdenesPendientes.Falla("api_no_disponible");
        }
    }
}
