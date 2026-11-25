namespace ObraSync.Usuarios.Application.Interfaces;

/// <summary>
/// Manda un correo de texto plano. Hoy se usa para el enlace de "olvidé mi contraseña".
/// Si no hay servidor de correo configurado (Correo__Host vacío), no envía nada: el enlace sigue saliendo por log.
/// </summary>
public interface ICorreoSaliente
{
    Task EnviarAsync(string destinatario, string asunto, string cuerpo, CancellationToken cancellationToken = default);
}
