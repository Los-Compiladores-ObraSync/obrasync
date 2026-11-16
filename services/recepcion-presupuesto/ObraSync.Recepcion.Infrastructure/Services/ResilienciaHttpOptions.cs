namespace ObraSync.Recepcion.Infrastructure.Services;

/// <summary>
/// Parámetros de resiliencia con Polly v8 (#103) para llamadas HTTP salientes a otros microservicios (ej. Usuarios y Obras).
/// Se pueden configurar en appsettings.json bajo "ResilienciaHttp" o usar los valores por defecto.
/// </summary>
public sealed class ResilienciaHttpOptions
{
    public const string SectionName = "ResilienciaHttp";

    /// <summary>Cantidad de reintentos ante fallas transitorias (por defecto 3).</summary>
    public int MaxReintentos { get; set; } = 3;

    /// <summary>Espera base en milisegundos antes del primer reintento con retroceso exponencial (por defecto 100 ms).</summary>
    public int EsperaBaseMs { get; set; } = 100;

    /// <summary>Si se aplica jitter aleatorio para mitigar el efecto manada (thundering herd).</summary>
    public bool UsarJitter { get; set; } = true;

    /// <summary>Proporción de fallas (0.0 a 1.0) para abrir el circuit breaker (por defecto 0.5 = 50%).</summary>
    public double RatioFallas { get; set; } = 0.5;

    /// <summary>Mínimo de pedidos dentro de la ventana para que el breaker pueda abrirse (evita abrir por un solo error, por defecto 5).</summary>
    public int MinimoPedidos { get; set; } = 5;

    /// <summary>Ventana de tiempo en segundos para registrar la tasa de fallas (por defecto 30 s).</summary>
    public int VentanaSegundos { get; set; } = 30;

    /// <summary>Tiempo en segundos que el breaker permanece abierto antes del estado semi-abierto (por defecto 15 s).</summary>
    public int PausaSegundos { get; set; } = 15;
}
