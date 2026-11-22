using MassTransit;
using Microsoft.Extensions.Logging;
using ObraSync.Contracts.Events;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Application.Interfaces;
using ObraSync.Usuarios.Domain.Entities;
using ObraSync.Usuarios.Infrastructure.Data;

namespace ObraSync.Usuarios.Infrastructure.Consumers;

public class MensajeChatEnviadoConsumer : IConsumer<MensajeChatEnviadoEvent>
{
    private readonly AuthDbContext _db;
    private readonly IDestinatariosNotificacionService _destinatarios;
    private readonly INotificacionPushService _pushService;
    private readonly ILogger<MensajeChatEnviadoConsumer> _logger;

    public MensajeChatEnviadoConsumer(
        AuthDbContext db,
        IDestinatariosNotificacionService destinatarios,
        INotificacionPushService pushService,
        ILogger<MensajeChatEnviadoConsumer> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _destinatarios = destinatarios ?? throw new ArgumentNullException(nameof(destinatarios));
        _pushService = pushService ?? throw new ArgumentNullException(nameof(pushService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Consume(ConsumeContext<MensajeChatEnviadoEvent> context)
    {
        var msg = context.Message;
        var correlationId = msg.CorrelationId != Guid.Empty
            ? msg.CorrelationId
            : (context.CorrelationId ?? Guid.Empty);

        using var logScope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        });

        if (msg.EventId == Guid.Empty ||
            msg.OrdenCompraId == Guid.Empty ||
            msg.Remitente is null ||
            msg.Destinatarios is null)
        {
            _logger.LogWarning(
                "MensajeChatEnviadoEvent inválido para push, se descarta. EventId {EventId}, OrdenCompraId {OrdenCompraId}, CorrelationId {CorrelationId}",
                msg.EventId, msg.OrdenCompraId, correlationId);
            return;
        }

        if (!await IdempotenciaConsumidorHelper.IntentarRegistrarEventoAsync(
                _db, msg.EventId, nameof(MensajeChatEnviadoEvent), correlationId, _logger, context.CancellationToken))
        {
            return;
        }

        var ahora = DateTime.UtcNow;
        var nombreRemitente = string.IsNullOrWhiteSpace(msg.Remitente.Nombre)
            ? "Usuario"
            : msg.Remitente.Nombre.Trim();
        var titulo = $"Nuevo mensaje de {nombreRemitente}";
        var cuerpo = string.IsNullOrWhiteSpace(msg.Fragmento)
            ? "Tenés un nuevo mensaje en el chat de una orden."
            : msg.Fragmento.Trim();

        var rolesSolicitados = msg.Destinatarios.Roles ?? Array.Empty<string>();
        var idsExplicitos = msg.Destinatarios.UsuarioIds ?? Array.Empty<Guid>();

        var notificadosPorRol = new HashSet<Guid>();
        if (rolesSolicitados.Count > 0)
        {
            var usuariosPorRol = await _destinatarios.GetUsuarioIdsPorRolesAsync(rolesSolicitados, context.CancellationToken);
            var filtrados = usuariosPorRol
                .Where(id => id != msg.Remitente.UsuarioId)
                .Distinct()
                .ToList();

            foreach (var id in filtrados)
            {
                notificadosPorRol.Add(id);
            }

            if (filtrados.Count > 0)
            {
                var rutaRol = rolesSolicitados.Any(r =>
                    string.Equals(r, UserRoles.Comprador, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(r, UserRoles.Admin, StringComparison.OrdinalIgnoreCase))
                    ? "/compras/ordenes"
                    : "/proveedores/mis-ordenes";

                await _pushService.EnviarAUsuariosAsync(
                    filtrados,
                    new PayloadNotificacionPushDto(
                        Titulo: titulo,
                        Cuerpo: cuerpo,
                        Ruta: rutaRol,
                        Tipo: "MensajeChatEnviado",
                        TimestampUtc: ahora),
                    context.CancellationToken);
            }
        }

        if (idsExplicitos.Count > 0)
        {
            var activosPorId = await _destinatarios.FiltrarUsuariosActivosAsync(idsExplicitos, context.CancellationToken);
            var pendientes = activosPorId
                .Where(id => id != msg.Remitente.UsuarioId && !notificadosPorRol.Contains(id))
                .Distinct()
                .ToList();

            if (pendientes.Count > 0)
            {
                await _pushService.EnviarAUsuariosAsync(
                    pendientes,
                    new PayloadNotificacionPushDto(
                        Titulo: titulo,
                        Cuerpo: cuerpo,
                        Ruta: "/proveedores/mis-ordenes",
                        Tipo: "MensajeChatEnviado",
                        TimestampUtc: ahora),
                    context.CancellationToken);
            }
        }
    }
}
