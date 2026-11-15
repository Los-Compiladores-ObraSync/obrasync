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
using ObraSync.Requisiciones.Application.Exceptions;
using ObraSync.Requisiciones.Application.Interfaces;

namespace ObraSync.Requisiciones.Infrastructure.Services
{
    /// <summary>
    /// Valida contra el microservicio Usuarios y Obras (GET /api/Obras) que la obra pertenezca
    /// al Capataz autenticado (#155), propagando su token JWT y el X-Correlation-ID sin acoplar bases de datos.
    /// UsuariosObras:BaseUrl es obligatoria (#178): si falta, el servicio no arranca (salvo pruebas aisladas).
    /// Si Usuarios y Obras no responde, lanza ObraAsignacionNoVerificableException (el controlador responde 503),
    /// que es distinto de "obra no asignada" (403).
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

        public HttpObraAsignacionValidator(
            IConfiguration configuration,
            HttpClient? httpClient = null,
            ILogger<HttpObraAsignacionValidator>? logger = null)
        {
            var baseUrl = configuration["UsuariosObras:BaseUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new InvalidOperationException(
                    "UsuariosObras:BaseUrl debe estar configurado para validar la obra asignada del Capataz (#178).");
            }

            _baseUrl = baseUrl;
            _httpClient = httpClient ?? SharedHttpClient;
            _logger = logger;
        }

        public async Task<bool> EstaAsignadaACapatazAsync(
            Guid obraId,
            string? authorizationHeader,
            string? correlationId,
            CancellationToken cancellationToken = default)
        {
            var endpoint = $"{_baseUrl.TrimEnd('/')}/api/Obras";
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);

            if (!string.IsNullOrWhiteSpace(authorizationHeader)
                && AuthenticationHeaderValue.TryParse(authorizationHeader, out var authHeader))
            {
                request.Headers.Authorization = authHeader;
            }

            if (!string.IsNullOrWhiteSpace(correlationId))
            {
                request.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);
            }

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                throw NoVerificable("no se pudo conectar", correlationId, ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // Se venció el tiempo de espera (no fue el cliente quien canceló).
                throw NoVerificable("no respondió a tiempo", correlationId, ex);
            }

            using (response)
            {
                // Sin permiso para consultar las obras: el Capataz no tiene esa obra asignada.
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    return false;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw NoVerificable($"respondió con el código {(int)response.StatusCode}", correlationId, null);
                }

                try
                {
                    var obras = await response.Content.ReadFromJsonAsync<List<ObraResumenDto>>(cancellationToken: cancellationToken);
                    return obras != null && obras.Any(o => o.Id == obraId);
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
}
