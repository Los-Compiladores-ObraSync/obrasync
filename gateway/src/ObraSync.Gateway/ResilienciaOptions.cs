namespace ObraSync.Gateway;

/// <summary>
/// Valores de la resiliencia del Gateway. Se leen de la sección "Resiliencia" de appsettings.json,
/// así se pueden ajustar sin recompilar. Los valores por defecto son una PROPUESTA: hay que acordarlos con el squad.
/// </summary>
public sealed class ResilienciaOptions
{
    /// <summary>Cuántas veces se reintenta un GET que falló (sin contar el primer intento).</summary>
    public int MaxReintentos { get; set; } = 3;

    /// <summary>Espera antes del primer reintento. Cada reintento espera el doble que el anterior (más un poco al azar).</summary>
    public int EsperaBaseMs { get; set; } = 200;

    /// <summary>Proporción de pedidos fallidos (de 0 a 1) a partir de la cual se abre el circuit breaker.</summary>
    public double RatioFallas { get; set; } = 0.5;

    /// <summary>Mínimo de pedidos dentro de la ventana para que el breaker pueda abrirse (evita abrir por 1 solo error).</summary>
    public int MinimoPedidos { get; set; } = 5;

    /// <summary>Ventana de tiempo, en segundos, en la que se miden las fallas.</summary>
    public int VentanaSegundos { get; set; } = 30;

    /// <summary>Cuánto tiempo, en segundos, queda abierto el breaker antes de dejar pasar un pedido de prueba.</summary>
    public int PausaSegundos { get; set; } = 15;
}
