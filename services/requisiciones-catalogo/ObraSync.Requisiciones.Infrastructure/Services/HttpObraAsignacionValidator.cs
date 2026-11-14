using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using ObraSync.Requisiciones.Application.Interfaces;

namespace ObraSync.Requisiciones.Infrastructure.Services
{
    /// <summary>
    /// Valida contra el microservicio Usuarios y Obras (GET /api/Obras) que la obra pertenezca
    /// al Capataz autenticado (#155), propagando su token JWT sin acoplar bases de datos.
    /// Si UsuariosObras:BaseUrl no está configurado (p. ej. pruebas aisladas del servicio),
    /// no bloquea la operación.
    /// </summary>
    public class HttpObraAsignacionValidator : IObraAsignacionValidator
    {
        private static readonly HttpClient SharedHttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        private readonly HttpClient _httpClient;
        private readonly string? _baseUrl;

        public HttpObraAsignacionValidator(IConfiguration configuration, HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? SharedHttpClient;
            _baseUrl = configuration["UsuariosObras:BaseUrl"];
        }

        public async Task<bool> EstaAsignadaACapatazAsync(
            Guid obraId,
            string? authorizationHeader,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_baseUrl))
            {
                return true;
            }

            var endpoint = $"{_baseUrl.TrimEnd('/')}/api/Obras";
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);

            if (!string.IsNullOrWhiteSpace(authorizationHeader)
                && AuthenticationHeaderValue.TryParse(authorizationHeader, out var authHeader))
            {
                request.Headers.Authorization = authHeader;
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var obras = await response.Content.ReadFromJsonAsync<List<ObraResumenDto>>(cancellationToken: cancellationToken);
            return obras != null && obras.Any(o => o.Id == obraId);
        }

        private sealed class ObraResumenDto
        {
            public Guid Id { get; set; }
        }
    }
}
