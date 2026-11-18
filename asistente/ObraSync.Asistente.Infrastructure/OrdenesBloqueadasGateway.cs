using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ObraSync.Asistente.Application;

namespace ObraSync.Asistente.Infrastructure;

/// <summary>Consulta las órdenes bloqueadas de Recepción con el JWT del usuario MCP actual.</summary>
public sealed class OrdenesBloqueadasGateway : IConsultorOrdenesBloqueadas
{
    public const int TamanoPagina = 50;
    public const int MaxBytes = 64 * 1024;
    public static readonly TimeSpan TiempoApi = TimeSpan.FromSeconds(3);

    private static readonly JsonSerializerOptions Opciones = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _http;
    private readonly ConfiguracionApi _config;
    private readonly IContextoDePeticion _contexto;

    public OrdenesBloqueadasGateway(HttpClient http, ConfiguracionApi config, IContextoDePeticion contexto)
    {
        _http = http;
        _config = config;
        _contexto = contexto;
    }

    public async Task<ResultadoOrdenesBloqueadas> ListarAsync(
        Guid obraId,
        int pagina,
        int tamano,
        CancellationToken ct)
    {
        if (obraId == Guid.Empty || pagina < 1 || tamano is < 1 or > TamanoPagina)
            return ResultadoOrdenesBloqueadas.Falla("argumentos_invalidos");

        var jwt = _contexto.Jwt;
        if (string.IsNullOrWhiteSpace(jwt))
            return ResultadoOrdenesBloqueadas.Falla("sin_permiso");

        var url = $"{_config.BaseUrl}/api/recepciones/ordenes-bloqueadas?obraId={obraId:D}&pagina={pagina}&tamano={tamano}";
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TiempoApi);
        using var pedido = new HttpRequestMessage(HttpMethod.Get, url);
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        pedido.Headers.TryAddWithoutValidation("X-Correlation-ID", _contexto.CorrelationId);

        try
        {
            using var respuesta = await _http.SendAsync(pedido, HttpCompletionOption.ResponseHeadersRead, limite.Token);
            if (respuesta.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return ResultadoOrdenesBloqueadas.Falla("sin_permiso");
            if (!respuesta.IsSuccessStatusCode)
                return ResultadoOrdenesBloqueadas.Falla("api_http_error");

            if (respuesta.Content.Headers.ContentLength is long longitud && longitud > MaxBytes)
                return ResultadoOrdenesBloqueadas.Falla("respuesta_demasiado_grande");

            var bytes = await LeerRespuestaAcotadaAsync(respuesta.Content, limite.Token);
            if (bytes is null)
                return ResultadoOrdenesBloqueadas.Falla("respuesta_demasiado_grande");

            if (!respuesta.Headers.TryGetValues("X-Total-Count", out var valores) ||
                !int.TryParse(valores.FirstOrDefault(), out var total) || total < 0)
                return ResultadoOrdenesBloqueadas.Falla("respuesta_invalida");

            var dtos = JsonSerializer.Deserialize<List<OrdenBloqueadaDto>>(bytes, Opciones);
            if (dtos is null)
                return ResultadoOrdenesBloqueadas.Falla("respuesta_invalida");

            var ordenes = dtos.Select(dto => new OrdenCompraBloqueadaAsistente(
                dto.OrdenCompraId,
                dto.ObraId,
                dto.Etapa,
                dto.MontoOrdenCompra,
                dto.MontoAsignadoPartida,
                dto.MontoComprometidoActual,
                dto.MontoProyectado,
                dto.Exceso,
                dto.Motivo,
                dto.PartidaId,
                dto.FechaBloqueo.Kind == DateTimeKind.Utc
                    ? dto.FechaBloqueo
                    : dto.FechaBloqueo.Kind == DateTimeKind.Local
                        ? dto.FechaBloqueo.ToUniversalTime()
                        : DateTime.SpecifyKind(dto.FechaBloqueo, DateTimeKind.Utc)))
                .ToList();

            return ResultadoOrdenesBloqueadas.Bien(ordenes, total);
        }
        catch (JsonException)
        {
            return ResultadoOrdenesBloqueadas.Falla("respuesta_invalida");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ResultadoOrdenesBloqueadas.Falla("api_timeout");
        }
        catch (HttpRequestException)
        {
            return ResultadoOrdenesBloqueadas.Falla("api_no_disponible");
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

    // Réplica únicamente del contrato JSON de Recepción; no se importa el DTO de otro servicio.
    private sealed record OrdenBloqueadaDto(
        Guid OrdenCompraId,
        Guid ObraId,
        Guid? PartidaId,
        string? Etapa,
        decimal MontoOrdenCompra,
        decimal MontoAsignadoPartida,
        decimal MontoComprometidoActual,
        decimal MontoProyectado,
        decimal Exceso,
        string Motivo,
        DateTime FechaBloqueo);
}
