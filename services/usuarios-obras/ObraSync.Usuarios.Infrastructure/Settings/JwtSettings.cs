namespace ObraSync.Usuarios.Infrastructure.Settings;

public class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Secret { get; set; } = string.Empty;
    public int ExpirationMinutes { get; set; } = 60;
    public string Issuer { get; set; } = "obrasync-usuarios";
    public string Audience { get; set; } = "obrasync-services";
}