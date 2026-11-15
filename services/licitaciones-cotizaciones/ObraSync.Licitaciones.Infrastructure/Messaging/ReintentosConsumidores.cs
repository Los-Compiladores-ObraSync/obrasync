using System;
using MassTransit;

namespace ObraSync.Licitaciones.Infrastructure.Messaging;

/// <summary>
/// Reintentos de los consumidores de eventos (tarjeta #179). Es la MISMA configuración en los servicios que consumen eventos.
/// Sin esto, un error pasajero (SQL Server todavía recuperándose, un timeout, un deadlock) manda el mensaje directo a la
/// cola &lt;nombre&gt;_error y el evento no se procesa: por ejemplo, la requisición aprobada nunca llega a Licitaciones.
/// Reintentar es seguro porque los consumidores son idempotentes por EventId (índice único en EventosProcesados).
/// Los eventos inválidos no se reintentan: los consumidores los descartan sin lanzar excepción.
/// </summary>
public static class ReintentosConsumidores
{
    /// <summary>Reintentos después del primer intento (en total, hasta 6 intentos).</summary>
    public const int Reintentos = 5;

    public static readonly TimeSpan EsperaMinima = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan EsperaMaxima = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan Incremento = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Reintentos con retroceso exponencial (espera cada vez más larga entre intentos, de 1 s hasta 30 s).
    /// Se usa con cfg.UsarReintentosExponenciales() ANTES de cfg.ConfigureEndpoints(context).
    /// Si se agotan los reintentos, el mensaje queda en la cola &lt;nombre&gt;_error.
    /// Los parámetros opcionales sirven para acelerar las pruebas.
    /// </summary>
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
