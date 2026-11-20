namespace ObraSync.Gateway;

/// <summary>
/// Límite por IP para los endpoints públicos de autenticación (login, registro, recuperar y restablecer contraseña).
/// Valor inicial propuesto: 10 pedidos por minuto. El equipo (Usuarios) debe confirmarlo.
/// </summary>
public sealed class AuthRateLimitOptions
{
    public int PermitLimit { get; set; } = 10;
    public int WindowSeconds { get; set; } = 60;
    public int SegmentsPerWindow { get; set; } = 6;
}
