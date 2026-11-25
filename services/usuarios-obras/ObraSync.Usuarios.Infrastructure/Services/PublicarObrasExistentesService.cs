using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ObraSync.Contracts.Events;
using ObraSync.Usuarios.Infrastructure.Data;

namespace ObraSync.Usuarios.Infrastructure.Services;

/// <summary>
/// Al iniciar, vuelve a publicar ObraCreadaEvent de las obras que ya existían, así también ellas reciben su
/// presupuesto semilla. Recepción es idempotente por obra y etapa: republicar no duplica partidas.
/// </summary>
public class PublicarObrasExistentesService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<PublicarObrasExistentesService> _logger;

    public PublicarObrasExistentesService(IServiceScopeFactory scopes, ILogger<PublicarObrasExistentesService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (var intento = 1; intento <= 5 && !stoppingToken.IsCancellationRequested; intento++)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10 * intento), stoppingToken);
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
                var publisher = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

                var obras = await db.Obras.AsNoTracking()
                    .Where(o => o.Activa)
                    .Select(o => new { o.Id, o.Nombre, Etapas = o.Etapas.Select(e => e.NombreEtapa).ToList() })
                    .ToListAsync(stoppingToken);

                foreach (var o in obras)
                {
                    await publisher.Publish(new ObraCreadaEvent(
                        Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), o.Id, o.Nombre, o.Etapas), stoppingToken);
                }

                _logger.LogInformation("Se republicó ObraCreadaEvent de {Cantidad} obras existentes.", obras.Count);
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudieron republicar las obras existentes (intento {Intento}).", intento);
            }
        }
    }
}
