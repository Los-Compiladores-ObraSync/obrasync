using System.Text.Json.Serialization;
using MassTransit;

namespace ObraSync.Requisiciones.Infrastructure.Messaging;

/// <summary>
/// Configuración común de los eventos en el bus (tarjeta #128). Es la MISMA en los tres servicios que publican o consumen eventos.
/// </summary>
public static class ConfiguracionJsonEventos
{
    /// <summary>
    /// MassTransit escribe por defecto los decimal como TEXTO ("10.5") para no perder precisión, pero los esquemas de
    /// contracts/events dicen "type": "number". Acá se le saca ese convertidor para que salgan como NÚMERO (10.5) y el
    /// mensaje cumpla el contrato. La lectura sigue aceptando número y texto, así un publicador viejo no rompe a los consumidores.
    /// Se usa con cfg.UsarDecimalesComoNumero() al configurar el bus, tanto en RabbitMQ como en memoria.
    /// </summary>
    public static void UsarDecimalesComoNumero(this IBusFactoryConfigurator configurador)
    {
        configurador.ConfigureJsonSerializerOptions(opciones =>
        {
            for (var i = opciones.Converters.Count - 1; i >= 0; i--)
            {
                if (opciones.Converters[i].GetType().Name == "StringDecimalJsonConverter")
                {
                    opciones.Converters.RemoveAt(i);
                }
            }

            opciones.NumberHandling = JsonNumberHandling.AllowReadingFromString;
            return opciones;
        });
    }
}
