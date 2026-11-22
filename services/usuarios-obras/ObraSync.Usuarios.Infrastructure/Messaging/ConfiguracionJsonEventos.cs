using System.Text.Json.Serialization;
using MassTransit;

namespace ObraSync.Usuarios.Infrastructure.Messaging;

/// <summary>
/// Configuración común de los eventos en el bus (tarjeta #128), alineada con Requisiciones, Licitaciones y Recepción.
/// </summary>
public static class ConfiguracionJsonEventos
{
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
