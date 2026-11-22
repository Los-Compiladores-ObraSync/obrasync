using MassTransit;
using Microsoft.Extensions.Logging;
using ObraSync.Contracts.Events;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Application.Interfaces;
using ObraSync.Usuarios.Infrastructure.Data;

namespace ObraSync.Usuarios.Infrastructure.Consumers;

public class OrdenCompraEmitidaConsumer : IConsumer<OrdenCompraEmitidaEvent>
{
    private readonly AuthDbContext _db;
    private readonly IDestinatariosNotificacionService _destinatarios;
    private readonly INotificacionPushService _pushService;
    private readonly ILogger<OrdenCompraEmitidaConsumer> _logger;

    public OrdenCompraEmitidaConsumer(
        AuthDbContext db,
        IDestinatariosNotificacionService destinatarios,
        INotificacionPushService pushService,
        ILogger<OrdenCompraEmitidaConsumer> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _destinatarios = destinatarios ?? throw new ArgumentNullException(nameof(destinatarios));
        _pushService = pushService ?? throw new ArgumentNullException(nameof(pushService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Consume(ConsumeContext<OrdenCompraEmitidaEvent> context)
    {
        var msg = context.Message;
        var correlationId = msg.CorrelationId != Guid.Empty
            ? msg.CorrelationId
            : (context.CorrelationId ?? Guid.Empty);

        using var logScope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        });

        if (msg.EventId == Guid.Empty || msg.OrdenCompraId == Guid.Empty)
        {
            _logger.LogWarning(
                "OrdenCompraEmitidaEvent inválido para push, se descarta. EventId {EventId}, OrdenCompraId {OrdenCompraId}, CorrelationId {CorrelationId}",
                msg.EventId, msg.OrdenCompraId, correlationId);
            return;
        }

        if (!await IdempotenciaConsumidorHelper.IntentarRegistrarEventoAsync(
                _db, msg.EventId, nameof(OrdenCompraEmitidaEvent), correlationId, _logger, context.CancellationToken))
        {
            return;
        }

        var ahora = DateTime.UtcNow;

        if (msg.ProveedorId != Guid.Empty &&
            await _destinatarios.EsUsuarioActivoAsync(msg.ProveedorId, context.CancellationToken))
        {
            await _pushService.EnviarAUsuariosAsync(
                new[] { msg.ProveedorId },
                new PayloadNotificacionPushDto(
                    Titulo: "Nueva orden de compra",
                    Cuerpo: "Te adjudicaron una nueva orden de compra.",
                    Ruta: "/proveedores/mis-ordenes",
                    Tipo: "OrdenCompraEmitida",
                    TimestampUtc: ahora),
                context.CancellationToken);
        }

        var capatazId = await _destinatarios.GetCapatazIdDeObraAsync(msg.ObraId, context.CancellationToken);
        if (capatazId.HasValue && capatazId.Value != msg.ProveedorId)
        {
            var rutaCapataz = msg.RequisicionId != Guid.Empty
                ? $"/obra/requisiciones/{msg.RequisicionId}/seguimiento"
                : "/obra";

            await _pushService.EnviarAUsuariosAsync(
                new[] { capatazId.Value },
                new PayloadNotificacionPushDto(
                    Titulo: "Orden de compra emitida",
                    Cuerpo: "Se emitió una orden de compra para tu obra.",
                    Ruta: rutaCapataz,
                    Tipo: "OrdenCompraEmitida",
                    TimestampUtc: ahora),
                context.CancellationToken);
        }
    }
}
