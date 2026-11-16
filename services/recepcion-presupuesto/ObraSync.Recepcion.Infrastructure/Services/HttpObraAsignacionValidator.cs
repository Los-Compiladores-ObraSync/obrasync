using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ObraSync.Recepcion.Application.Exceptions;
using ObraSync.Recepcion.Application.Interfaces;
using Polly;
using Polly.CircuitBreaker;

namespace ObraSync.Recepcion.Infrastructure.Services;

/// <summary>
/// Valida contra el microservicio Usuarios y Obras (GET /api/Obras) que la obra pertenezca
/// al Capataz autenticado (#189, #155), propagando su token JWT y el X-Correlation-ID (#104).
/// Protegido con resiliencia Polly v8 (#103): reintentos con retroceso exponencial ante fallas transitorias
/// y circuit breaker que corta rápido si el servicio destino está caído.
/// Si Usuarios y Obras no responde, da timeout o el breaker está abierto, lanza ObraAsignacionNoVerificableException
/// (503 Service Unavailable), lo cual es distinto a una obra rechazada (403 Forbidden).
/// </summary>
public class HttpObraAsignacionValidator : IObraAsignacionValidator
{
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly ILogger<HttpObraAsignacionValidator>? _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    public HttpObraAsignacionValidator(
        IConfiguration configuration,
        HttpClient? httpClient = null,
        ILogger<HttpObraAsignacionValidator>? logger = null,
        ResiliencePipeline<HttpResponseMessage>? pipeline = null)
    {
        var baseUrl = configuration["UsuariosObras:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException(
                "UsuariosObras:BaseUrl debe estar configurado para validar la obra asignada del Capataz (#189, #178).");
        }

        _baseUrl = baseUrl;
        _httpClient = httpClient ?? SharedHttpClient;
        _logger = logger;
        _pipeline = pipeline ?? PipelineResilienciaHttp.Crear();
    }

    public async Task<bool> EstaAsignadaACapatazAsync(
        Guid obraId,
        string? authorizationHeader,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        var obras = await ObtenerObrasAsignadasAsync(authorizationHeader, correlationId, cancellationToken);
        return obras != null && obras.Contains(obraId);
    }

    public async Task<IReadOnlyList<Guid>?> ObtenerObrasAsignadasAsync(
        string? authorizationHeader,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        var endpoint = $"{_baseUrl.TrimEnd('/')}/api/Obras";

        HttpResponseMessage response;
        try
        {
            // Ejecutar a través del pipeline de Polly (#103):
            // Cada reintento genera una nueva instancia de HttpRequestMessage para evitar errores de request ya enviado.
            response = await _pipeline.ExecuteAsync(async state =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, endpoint);

                if (!string.IsNullOrWhiteSpace(authorizationHeader)
                    && AuthenticationHeaderValue.TryParse(authorizationHeader, out var authHeader))
                {
                    request.Headers.Authorization = authHeader;
                }

                if (!string.IsNullOrWhiteSpace(correlationId))
                {
                    request.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);
                }

                return await _httpClient.SendAsync(request, state);
            }, cancellationToken);
        }
        catch (BrokenCircuitException ex)
        {
            throw NoVerificable("circuit breaker abierto por fallas reiteradas", correlationId, ex);
        }
        catch (HttpRequestException ex)
        {
            throw NoVerificable("no se pudo conectar", correlationId, ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw NoVerificable("no respondió a tiempo", correlationId, ex);
        }

        using (response)
        {
            // Sin permiso para consultar las obras: el Capataz no tiene esa obra asignada
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return Array.Empty<Guid>();
            }

            if (!response.IsSuccessStatusCode)
            {
                throw NoVerificable($"respondió con el código {(int)response.StatusCode}", correlationId, null);
            }

            try
            {
                var obras = await response.Content.ReadFromJsonAsync<List<ObraResumenDto>>(cancellationToken: cancellationToken);
                return obras?.Select(o => o.Id).ToList() ?? (IReadOnlyList<Guid>)Array.Empty<Guid>();
            }
            catch (JsonException ex)
            {
                throw NoVerificable("devolvió una respuesta que no se pudo leer", correlationId, ex);
            }
        }
    }

    private ObraAsignacionNoVerificableException NoVerificable(string motivo, string? correlationId, Exception? inner)
    {
        _logger?.LogWarning(
            inner,
            "No se pudo verificar la obra asignada: Usuarios y Obras {Motivo}. CorrelationId={CorrelationId}",
            motivo,
            correlationId);

        return new ObraAsignacionNoVerificableException($"Usuarios y Obras {motivo}.", inner);
    }

    private sealed class ObraResumenDto
    {
        public Guid Id { get; set; }
    }
}
