using MassTransit;
using Microsoft.Extensions.Logging;
using ObraSync.Contracts.Events;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Application.Interfaces;
using ObraSync.Usuarios.Domain.Entities;
using ObraSync.Usuarios.Infrastructure.Data;

namespace ObraSync.Usuarios.Infrastructure.Consumers;

public class OrdenCompraDespachadaConsumer : IConsumer<OrdenCompraDespachadaEvent>
{
    private static readonly string[] RolesCompras = [UserRoles.Comprador, UserRoles.Admin];

    private readonly AuthDbContext _db;
    private readonly IDestinatariosNotificacionService _destinatarios;
    private readonly INotificacionPushService _pushService;
    private readonly ILogger<OrdenCompraDespachadaConsumer> _logger;

    public OrdenCompraDespachadaConsumer(
        AuthDbContext db,
        IDestinatariosNotificacionService destinatarios,
        INotificacionPushService pushService,
        ILogger<OrdenCompraDespachadaConsumer> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _destinatarios = destinatarios ?? throw new ArgumentNullException(nameof(destinatarios));
        _pushService = pushService ?? throw new ArgumentNullException(nameof(pushService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Consume(ConsumeContext<OrdenCompraDespachadaEvent> context)
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
                "OrdenCompraDespachadaEvent inválido para push, se descarta. EventId {EventId}, OrdenCompraId {OrdenCompraId}, CorrelationId {CorrelationId}",
                msg.EventId, msg.OrdenCompraId, correlationId);
            return;
        }

        if (!await IdempotenciaConsumidorHelper.IntentarRegistrarEventoAsync(
                _db, msg.EventId, nameof(OrdenCompraDespachadaEvent), correlationId, _logger, context.CancellationToken))
        {
            return;
        }

        var ahora = DateTime.UtcNow;

        var capatazId = await _destinatarios.GetCapatazIdDeObraAsync(msg.ObraId, context.CancellationToken);
        if (capatazId.HasValue)
        {
            // Si es un re-despacho (#248) el Capataz ya recibió una parte: se le aclara que viene el resto.
            var queSeDespacho = msg.EsRedespacho ? "el resto de un pedido" : "un pedido";
            var cuerpoCapataz = msg.FechaEstimadaLlegada.HasValue
                ? $"El proveedor despachó {queSeDespacho} para tu obra (llegada est.: {msg.FechaEstimadaLlegada.Value:dd/MM})."
                : $"El proveedor despachó {queSeDespacho} para tu obra.";

            await _pushService.EnviarAUsuariosAsync(
                new[] { capatazId.Value },
                new PayloadNotificacionPushDto(
                    Titulo: "Pedido en camino",
                    Cuerpo: cuerpoCapataz,
                    Ruta: "/obra/recepcion",
                    Tipo: "OrdenCompraDespachada",
                    TimestampUtc: ahora),
                context.CancellationToken);
        }

        var comprasIds = await _destinatarios.GetUsuarioIdsPorRolesAsync(RolesCompras, context.CancellationToken);
        if (comprasIds.Count > 0)
        {
            var restantes = capatazId.HasValue
                ? comprasIds.Where(id => id != capatazId.Value).ToList()
                : comprasIds;

            if (restantes.Count > 0)
            {
                await _pushService.EnviarAUsuariosAsync(
                    restantes,
                    new PayloadNotificacionPushDto(
                        Titulo: "Orden de compra despachada",
                        Cuerpo: "El proveedor marcó el despacho de una orden de compra.",
                        Ruta: "/compras/ordenes",
                        Tipo: "OrdenCompraDespachada",
                        TimestampUtc: ahora),
                    context.CancellationToken);
            }
        }
    }
}
