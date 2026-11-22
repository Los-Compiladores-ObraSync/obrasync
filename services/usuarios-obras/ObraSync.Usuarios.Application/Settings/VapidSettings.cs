namespace ObraSync.Usuarios.Application.Settings;

public class VapidSettings
{
    public const string SectionName = "Vapid";

    public string PublicKey { get; set; } = string.Empty;
    public string PrivateKey { get; set; } = string.Empty;
    public string Subject { get; set; } = "mailto:soporte@obrasync.local";

    public bool TieneClavePublica => !string.IsNullOrWhiteSpace(PublicKey);

    public bool EstaConfigurado() =>
        !string.IsNullOrWhiteSpace(PublicKey)
        && !string.IsNullOrWhiteSpace(PrivateKey)
        && !string.IsNullOrWhiteSpace(Subject);
}
