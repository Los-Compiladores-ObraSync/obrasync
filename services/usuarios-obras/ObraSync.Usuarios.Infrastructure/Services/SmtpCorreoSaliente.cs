using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ObraSync.Usuarios.Application.Interfaces;
using ObraSync.Usuarios.Application.Settings;

namespace ObraSync.Usuarios.Infrastructure.Services;

/// <summary>
/// Manda correos por SMTP con la clase del propio .NET (sin paquetes nuevos).
/// Sin Correo__Host configurado no hace nada: el enlace de recuperación sigue saliendo por log (decisión #51).
/// </summary>
public class SmtpCorreoSaliente : ICorreoSaliente
{
    private readonly CorreoSettings _settings;
    private readonly ILogger<SmtpCorreoSaliente> _logger;

    public SmtpCorreoSaliente(IOptions<CorreoSettings> options, ILogger<SmtpCorreoSaliente> logger)
    {
        _settings = options.Value;
        _logger = logger;
    }

    public async Task EnviarAsync(string destinatario, string asunto, string cuerpo, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.Host))
        {
            _logger.LogDebug("Correo no enviado: no hay servidor SMTP configurado (Correo__Host vacío).");
            return;
        }

        using var mensaje = new MailMessage(_settings.Remitente, destinatario, asunto, cuerpo);
        // Mailpit no usa TLS ni usuario; para un SMTP real habría que sumar credenciales y SSL (decisión del equipo).
        using var cliente = new SmtpClient(_settings.Host, _settings.Port) { EnableSsl = false };
        await cliente.SendMailAsync(mensaje, cancellationToken);
    }
}
