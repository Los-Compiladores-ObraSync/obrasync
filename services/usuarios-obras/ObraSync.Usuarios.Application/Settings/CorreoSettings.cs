namespace ObraSync.Usuarios.Application.Settings;

// Servidor SMTP para mandar correos. Se lee de la sección "Correo" y se pisa con variables de entorno
// (Correo__Host, Correo__Port, Correo__Remitente). Si Host está vacío, no se manda ningún correo.
// En desarrollo se usa Mailpit (docker compose): recibe todo y lo muestra en http://localhost:8025, sin mandar nada afuera.
public class CorreoSettings
{
    public const string SectionName = "Correo";

    public string Host { get; set; } = string.Empty;

    // Puerto SMTP de Mailpit.
    public int Port { get; set; } = 1025;

    public string Remitente { get; set; } = "no-responder@obrasync.local";
}
