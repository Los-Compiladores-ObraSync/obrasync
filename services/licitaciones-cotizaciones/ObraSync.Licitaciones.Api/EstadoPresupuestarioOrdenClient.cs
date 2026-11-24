using System.Net.Http.Json;
using System.Text.Json;
using ObraSync.Licitaciones.Application.Interfaces;

public sealed class EstadoPresupuestarioOrdenClient(
    IHttpClientFactory httpClientFactory,
    IHttpContextAccessor httpContextAccessor) : IEstadoPresupuestarioOrden
{
    public async Task<bool?> EstaBloqueadaAsync(
        Guid ordenCompraId,
        CancellationToken cancellationToken)
    {
        var authorization = httpContextAccessor.HttpContext?
            .Request.Headers["Authorization"].ToString();

        if (string.IsNullOrWhiteSpace(authorization))
        {
            return null;
        }

        try
        {
            using var client = httpClientFactory.CreateClient("RecepcionPresupuesto");
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"api/recepciones/ordenes/{ordenCompraId}");

            request.Headers.TryAddWithoutValidation("Authorization", authorization);

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var estado = await response.Content.ReadFromJsonAsync<RespuestaPresupuesto>(
                cancellationToken: cancellationToken);

            return estado?.BloqueadaPorPresupuesto;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private sealed record RespuestaPresupuesto(bool? BloqueadaPorPresupuesto);
}