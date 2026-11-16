using Microsoft.Extensions.Configuration;

namespace ObraSync.Asistente.Infrastructure;

/// <summary>
/// Datos para hablar con el gateway de IA del profe.
/// La clave y la URL llegan SOLO por configuración (variables de entorno o user-secrets):
/// nunca se escriben en el código, el Dockerfile ni el repositorio.
/// </summary>
public sealed class ConfiguracionIA
{
    public string BaseUrl { get; }
    public string Deployment { get; }
    public string ApiVersion { get; }
    public string ApiKey { get; }

    /// <summary>
    /// Deployment para leer remitos por foto (#209). Si no se define, usa el mismo del chat. Separarlo permite que el profe
    /// despliegue un modelo más potente para el OCR sin tocar el chat ni el código: solo se cambia Ai__DeploymentRemito.
    /// </summary>
    public string DeploymentRemito { get; }

    public ConfiguracionIA(string? baseUrl, string? deployment, string? apiVersion, string? apiKey, string? deploymentRemito = null)
    {
        BaseUrl = (baseUrl ?? "").Trim().TrimEnd('/');
        Deployment = string.IsNullOrWhiteSpace(deployment) ? "gpt-4.1-mini" : deployment.Trim();
        ApiVersion = string.IsNullOrWhiteSpace(apiVersion) ? "2025-03-01-preview" : apiVersion.Trim();
        ApiKey = (apiKey ?? "").Trim();
        DeploymentRemito = string.IsNullOrWhiteSpace(deploymentRemito) ? Deployment : deploymentRemito.Trim();
    }

    /// <summary>Sin clave o sin URL el asistente queda "apagado" (503), pero el servicio igual arranca.</summary>
    public bool EstaActivo => BaseUrl.Length > 0 && ApiKey.Length > 0;

    /// <summary>
    /// Lee Ai:ApiKey, Ai:BaseUrl, Ai:Deployment y Ai:ApiVersion (en Docker: Ai__ApiKey, etc.).
    /// Si Ai:ApiKey o Ai:BaseUrl no están, prueba con AI_API_KEY y AI_BASE_URL (comodidad al correr desde Visual Studio).
    /// </summary>
    public static ConfiguracionIA Desde(IConfiguration configuracion) => new(
        PrimeroConValor(configuracion["Ai:BaseUrl"], Environment.GetEnvironmentVariable("AI_BASE_URL")),
        configuracion["Ai:Deployment"],
        configuracion["Ai:ApiVersion"],
        PrimeroConValor(configuracion["Ai:ApiKey"], Environment.GetEnvironmentVariable("AI_API_KEY")),
        configuracion["Ai:DeploymentRemito"]);

    private static string? PrimeroConValor(string? a, string? b) => string.IsNullOrWhiteSpace(a) ? b : a;

    public string UrlChat =>
        $"{BaseUrl}/openai/deployments/{Uri.EscapeDataString(Deployment)}/chat/completions?api-version={ApiVersion}";

    // Para que la clave no aparezca si alguien imprime este objeto por error.
    public override string ToString() => $"ConfiguracionIA(Deployment={Deployment}, DeploymentRemito={DeploymentRemito}, ApiKey=***)";
}
