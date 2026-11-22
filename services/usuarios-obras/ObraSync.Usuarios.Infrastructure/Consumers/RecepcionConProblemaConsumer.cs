using MassTransit;
using Microsoft.Extensions.Logging;
using ObraSync.Contracts.Events;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Application.Interfaces;
using ObraSync.Usuarios.Domain.Entities;
using ObraSync.Usuarios.Infrastructure.Data;

namespace ObraSync.Usuarios.Infrastructure.Consumers;

public class RecepcionConProblemaConsumer : IConsumer<RecepcionConProblemaEvent>
{
    private static readonly string[] RolesCompras = [UserRoles.Comprador, UserRoles.Admin];

    private readonly AuthDbContext _db;
    private readonly IDestinatariosNotificacionService _destinatarios;
    private readonly INotificacionPushService _pushService;
    private readonly ILogger<RecepcionConProblemaConsumer> _logger;

    public RecepcionConProblemaConsumer(
        AuthDbContext db,
        IDestinatariosNotificacionService destinatarios,
        INotificacionPushService pushService,
        ILogger<RecepcionConProblemaConsumer> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _destinatarios = destinatarios ?? throw new ArgumentNullException(nameof(destinatarios));
        _pushService = pushService ?? throw new ArgumentNullException(nameof(pushService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Consume(ConsumeContext<RecepcionConProblemaEvent> context)
    {
        var msg = context.Message;
        var correlationId = msg.CorrelationId != Guid.Empty
            ? msg.CorrelationId
            : (context.CorrelationId ?? Guid.Empty);

        using var logScope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        });

        if (msg.EventId == Guid.Empty || msg.RecepcionId == Guid.Empty || string.IsNullOrWhiteSpace(msg.Motivo))
        {
            _logger.LogWarning(
                "RecepcionConProblemaEvent inválido para push, se descarta. EventId {EventId}, RecepcionId {RecepcionId}, CorrelationId {CorrelationId}",
                msg.EventId, msg.RecepcionId, correlationId);
            return;
        }

        if (!await IdempotenciaConsumidorHelper.IntentarRegistrarEventoAsync(
                _db, msg.EventId, nameof(RecepcionConProblemaEvent), correlationId, _logger, context.CancellationToken))
        {
            return;
        }

        // Privacidad (#227): usar únicamente el motivo tipificado (no incluir detalle libre ni datos sensibles).
        var motivoLimpio = msg.Motivo.Trim();
        var ahora = DateTime.UtcNow;

        var comprasIds = await _destinatarios.GetUsuarioIdsPorRolesAsync(RolesCompras, context.CancellationToken);
        if (comprasIds.Count > 0)
        {
            await _pushService.EnviarAUsuariosAsync(
                comprasIds,
                new PayloadNotificacionPushDto(
                    Titulo: "Problema en recepción",
                    Cuerpo: $"El capataz registró un problema: {motivoLimpio}.",
                    Ruta: "/compras/ordenes",
                    Tipo: "RecepcionConProblema",
                    TimestampUtc: ahora),
                context.CancellationToken);
        }

        if (msg.ProveedorId is Guid proveedorId &&
            proveedorId != Guid.Empty &&
            !comprasIds.Contains(proveedorId) &&
            await _destinatarios.EsUsuarioActivoAsync(proveedorId, context.CancellationToken))
        {
            await _pushService.EnviarAUsuariosAsync(
                new[] { proveedorId },
                new PayloadNotificacionPushDto(
                    Titulo: "Problema en entrega",
                    Cuerpo: $"Se reportó un problema en una entrega: {motivoLimpio}.",
                    Ruta: "/proveedores/mis-ordenes",
                    Tipo: "RecepcionConProblema",
                    TimestampUtc: ahora),
                context.CancellationToken);
        }
    }
}
