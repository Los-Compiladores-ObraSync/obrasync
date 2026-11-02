namespace ObraSync.Usuarios.Application.Settings;

// Configuración del frontend que necesita el backend (por ejemplo, para armar enlaces).
// Se lee de la sección "Frontend" de appsettings.json y se pisa con la variable de entorno Frontend__BaseUrl.
public class FrontendSettings
{
    public const string SectionName = "Frontend";

    // Puerto por defecto de Vite. El puerto del frontend todavía no está fijado por el equipo:
    // si cambia, alcanza con definir Frontend__BaseUrl (no hace falta tocar código).
    public const string BaseUrlPorDefecto = "http://localhost:5173";

    public string BaseUrl { get; set; } = BaseUrlPorDefecto;
}
