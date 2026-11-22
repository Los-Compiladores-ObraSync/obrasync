using MassTransit;
using Microsoft.Extensions.Logging;
using ObraSync.Contracts.Events;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Application.Interfaces;
using ObraSync.Usuarios.Domain.Entities;
using ObraSync.Usuarios.Infrastructure.Data;

namespace ObraSync.Usuarios.Infrastructure.Consumers;

public class RecepcionConfirmadaConsumer : IConsumer<RecepcionConfirmadaEvent>
{
    private static readonly string[] RolesCompras = [UserRoles.Comprador, UserRoles.Admin];

    private readonly AuthDbContext _db;
    private readonly IDestinatariosNotificacionService _destinatarios;
    private readonly INotificacionPushService _pushService;
    private readonly ILogger<RecepcionConfirmadaConsumer> _logger;

    public RecepcionConfirmadaConsumer(
        AuthDbContext db,
        IDestinatariosNotificacionService destinatarios,
        INotificacionPushService pushService,
        ILogger<RecepcionConfirmadaConsumer> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _destinatarios = destinatarios ?? throw new ArgumentNullException(nameof(destinatarios));
        _pushService = pushService ?? throw new ArgumentNullException(nameof(pushService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Consume(ConsumeContext<RecepcionConfirmadaEvent> context)
    {
        var msg = context.Message;
        var correlationId = msg.CorrelationId != Guid.Empty
            ? msg.CorrelationId
            : (context.CorrelationId ?? Guid.Empty);

        using var logScope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        });

        if (msg.EventId == Guid.Empty || msg.RecepcionId == Guid.Empty)
        {
            _logger.LogWarning(
                "RecepcionConfirmadaEvent inválido para push, se descarta. EventId {EventId}, RecepcionId {RecepcionId}, CorrelationId {CorrelationId}",
                msg.EventId, msg.RecepcionId, correlationId);
            return;
        }

        if (!await IdempotenciaConsumidorHelper.IntentarRegistrarEventoAsync(
                _db, msg.EventId, nameof(RecepcionConfirmadaEvent), correlationId, _logger, context.CancellationToken))
        {
            return;
        }

        var ahora = DateTime.UtcNow;
        var comprasIds = await _destinatarios.GetUsuarioIdsPorRolesAsync(RolesCompras, context.CancellationToken);

        if (comprasIds.Count > 0)
        {
            var sufijoRemito = string.IsNullOrWhiteSpace(msg.NumeroRemito)
                ? string.Empty
                : $" (remito {msg.NumeroRemito.Trim()})";
            var cuerpoCompras = msg.OrdenCompleta
                ? $"Se confirmó recepción completa en obra{sufijoRemito}."
                : $"Se confirmó recepción parcial en obra{sufijoRemito}.";

            await _pushService.EnviarAUsuariosAsync(
                comprasIds,
                new PayloadNotificacionPushDto(
                    Titulo: "Recepción confirmada",
                    Cuerpo: cuerpoCompras,
                    Ruta: "/compras/ordenes",
                    Tipo: "RecepcionConfirmada",
                    TimestampUtc: ahora),
                context.CancellationToken);
        }

        // Compatibilidad futura: si Recepción incorpora ProveedorId en el evento, se notifica también al Proveedor titular.
        if (msg.ProveedorId is Guid proveedorId &&
            proveedorId != Guid.Empty &&
            await _destinatarios.EsUsuarioActivoAsync(proveedorId, context.CancellationToken))
        {
            var cuerpoProveedor = msg.OrdenCompleta
                ? "El capataz confirmó la recepción completa de tu entrega."
                : "El capataz confirmó una recepción parcial de tu entrega.";

            await _pushService.EnviarAUsuariosAsync(
                new[] { proveedorId },
                new PayloadNotificacionPushDto(
                    Titulo: "Entrega confirmada",
                    Cuerpo: cuerpoProveedor,
                    Ruta: "/proveedores/mis-ordenes",
                    Tipo: "RecepcionConfirmada",
                    TimestampUtc: ahora),
                context.CancellationToken);
        }
    }
}
