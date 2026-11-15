using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using ObraSync.Licitaciones.Infrastructure.Messaging;
using Xunit;

namespace ObraSync.Licitaciones.Tests;

/// <summary>
/// Reintentos de los consumidores (#179). Se prueba con un bus en memoria y esperas cortas:
/// un consumidor que falla con un error pasajero tiene que reintentarse, y no tiene que reintentar para siempre.
/// </summary>
public class ReintentosConsumidoresTests
{
    public record EventoDePrueba(Guid Id);

    public sealed class Contador
    {
        private int _intentos;

        public int Intentos => Volatile.Read(ref _intentos);
        public int FallasAntesDeFuncionar { get; init; }
        public TaskCompletionSource Procesado { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Registrar() => Interlocked.Increment(ref _intentos);
    }

    public sealed class ConsumidorDePrueba : IConsumer<EventoDePrueba>
    {
        private readonly Contador _contador;

        public ConsumidorDePrueba(Contador contador)
        {
            _contador = contador;
        }

        public Task Consume(ConsumeContext<EventoDePrueba> context)
        {
            var intento = _contador.Registrar();
            if (intento <= _contador.FallasAntesDeFuncionar)
            {
                throw new InvalidOperationException("Error pasajero simulado (por ejemplo, SQL Server recuperándose)");
            }

            _contador.Procesado.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private static ServiceProvider Armar(Contador contador, int reintentos)
        => new ServiceCollection()
            .AddLogging()
            .AddSingleton(contador)
            .AddMassTransit(x =>
            {
                x.AddConsumer<ConsumidorDePrueba>();
                x.UsingInMemory((context, cfg) =>
                {
                    cfg.UsarReintentosExponenciales(
                        reintentos,
                        TimeSpan.FromMilliseconds(10),
                        TimeSpan.FromMilliseconds(50),
                        TimeSpan.FromMilliseconds(10));
                    cfg.ConfigureEndpoints(context);
                });
            })
            .BuildServiceProvider();

    [Fact]
    public async Task ErrorPasajero_SeReintentaYAlFinalProcesaElMensaje()
    {
        var contador = new Contador { FallasAntesDeFuncionar = 2 };
        await using var provider = Armar(contador, reintentos: 3);
        var bus = provider.GetRequiredService<IBusControl>();
        await bus.StartAsync();
        try
        {
            await bus.Publish(new EventoDePrueba(Guid.NewGuid()));

            var termino = await Task.WhenAny(contador.Procesado.Task, Task.Delay(TimeSpan.FromSeconds(10)));

            Assert.Same(contador.Procesado.Task, termino);
            Assert.Equal(3, contador.Intentos); // 2 fallas + 1 intento que funciona
        }
        finally
        {
            await bus.StopAsync();
        }
    }

    [Fact]
    public async Task ErrorQueNuncaSeArregla_SeReintentaSoloLasVecesConfiguradas()
    {
        var contador = new Contador { FallasAntesDeFuncionar = int.MaxValue };
        await using var provider = Armar(contador, reintentos: 3);
        var bus = provider.GetRequiredService<IBusControl>();
        await bus.StartAsync();
        try
        {
            await bus.Publish(new EventoDePrueba(Guid.NewGuid()));

            var limite = DateTime.UtcNow.AddSeconds(10);
            while (contador.Intentos < 4 && DateTime.UtcNow < limite)
            {
                await Task.Delay(20);
            }

            // Margen para comprobar que no hay un intento más después del último reintento
            await Task.Delay(300);

            Assert.Equal(4, contador.Intentos); // 1 intento inicial + 3 reintentos
            Assert.False(contador.Procesado.Task.IsCompleted);
        }
        finally
        {
            await bus.StopAsync();
        }
    }

    [Fact]
    public void ConfiguracionPorDefecto_EsDeCincoReintentosDeUnoATreintaSegundos()
    {
        Assert.Equal(5, ReintentosConsumidores.Reintentos);
        Assert.Equal(TimeSpan.FromSeconds(1), ReintentosConsumidores.EsperaMinima);
        Assert.Equal(TimeSpan.FromSeconds(30), ReintentosConsumidores.EsperaMaxima);
    }
}
