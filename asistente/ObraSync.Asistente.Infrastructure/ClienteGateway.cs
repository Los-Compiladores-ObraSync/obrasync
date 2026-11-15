using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ObraSync.Asistente.Application;

namespace ObraSync.Asistente.Infrastructure;

/// <summary>
/// Habla con el gateway de IA del profe (Chat Completions).
/// A propósito SIN reintentos automáticos: cada intento gasta cupo compartido y un 429 no se arregla repitiendo.
/// </summary>
public sealed class ClienteGateway : IModeloChat
{
    private readonly HttpClient _http;
    private readonly ConfiguracionIA _config;

    public ClienteGateway(HttpClient http, ConfiguracionIA config)
    {
        _http = http;
        _config = config;
    }

    public async Task<RespuestaModelo> PedirAsync(
        JsonArray mensajes, JsonArray? herramientas, int maxTokensSalida, CancellationToken ct)
    {
        if (!_config.EstaActivo)
            throw new ModeloException("asistente_apagado");

        // DeepClone: un nodo JSON no puede tener dos "padres" a la vez.
        var cuerpo = new JsonObject
        {
            ["messages"] = mensajes.DeepClone(),
            ["max_completion_tokens"] = maxTokensSalida
        };
        if (herramientas is not null)
        {
            cuerpo["tools"] = herramientas.DeepClone();
            cuerpo["tool_choice"] = "auto";
        }

        using var pedido = new HttpRequestMessage(HttpMethod.Post, _config.UrlChat);
        pedido.Headers.Add("api-key", _config.ApiKey);
        pedido.Content = new StringContent(cuerpo.ToJsonString(), Encoding.UTF8, "application/json");

        HttpResponseMessage respuesta;
        try
        {
            respuesta = await _http.SendAsync(pedido, ct);
        }
        catch (HttpRequestException)
        {
            throw new ModeloException("modelo_no_disponible");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            // Cancelado por el timeout del HttpClient, no por el tiempo total del agente.
            throw new ModeloException("modelo_timeout");
        }

        using (respuesta)
        {
            switch ((int)respuesta.StatusCode)
            {
                case 401: throw new ModeloException("modelo_clave_invalida");
                case 403: throw new ModeloException("modelo_sin_permiso_o_cupo");
                case 404: throw new ModeloException("modelo_no_encontrado");
                case 429: throw new ModeloException("modelo_limite_de_tasa");
            }
            if (!respuesta.IsSuccessStatusCode)
                throw new ModeloException("modelo_error_http");

            var texto = await respuesta.Content.ReadAsStringAsync(ct);
            try
            {
                var raiz = JsonNode.Parse(texto)!;
                var eleccion = raiz["choices"]![0]!;
                var mensaje = eleccion["message"]!.AsObject().DeepClone().AsObject();
                var fin = eleccion["finish_reason"]?.GetValue<string>() ?? "";
                var tokens = raiz["usage"]?["total_tokens"]?.GetValue<int>() ?? 0;
                return new RespuestaModelo(mensaje, fin, tokens);
            }
            catch (Exception ex) when (ex is JsonException or NullReferenceException
                                          or InvalidOperationException or FormatException)
            {
                throw new ModeloException("modelo_respuesta_invalida");
            }
        }
    }
}
