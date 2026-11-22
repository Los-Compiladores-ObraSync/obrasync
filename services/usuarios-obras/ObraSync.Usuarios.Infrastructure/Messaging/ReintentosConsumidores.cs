using MassTransit;

namespace ObraSync.Usuarios.Infrastructure.Messaging;

/// <summary>
/// Reintentos exponenciales para los consumidores MassTransit (misma política que #179).
/// </summary>
public static class ReintentosConsumidores
{
    public const int Reintentos = 5;

    public static readonly TimeSpan EsperaMinima = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan EsperaMaxima = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan Incremento = TimeSpan.FromSeconds(2);

    public static void UsarReintentosExponenciales(
        this IConsumePipeConfigurator configurador,
        int reintentos = Reintentos,
        TimeSpan? esperaMinima = null,
        TimeSpan? esperaMaxima = null,
        TimeSpan? incremento = null)
    {
        configurador.UseMessageRetry(r => r.Exponential(
            reintentos,
            esperaMinima ?? EsperaMinima,
            esperaMaxima ?? EsperaMaxima,
            incremento ?? Incremento));
    }
}
