using MassTransit;
using Microsoft.Extensions.Logging;
using ObraSync.Contracts.Events;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Application.Interfaces;
using ObraSync.Usuarios.Domain.Entities;
using ObraSync.Usuarios.Infrastructure.Data;

namespace ObraSync.Usuarios.Infrastructure.Consumers;

public class RequisicionAprobadaConsumer : IConsumer<RequisicionAprobadaEvent>
{
    private static readonly string[] RolesCompras = [UserRoles.Comprador, UserRoles.Admin];

    private readonly AuthDbContext _db;
    private readonly IDestinatariosNotificacionService _destinatarios;
    private readonly INotificacionPushService _pushService;
    private readonly ILogger<RequisicionAprobadaConsumer> _logger;

    public RequisicionAprobadaConsumer(
        AuthDbContext db,
        IDestinatariosNotificacionService destinatarios,
        INotificacionPushService pushService,
        ILogger<RequisicionAprobadaConsumer> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _destinatarios = destinatarios ?? throw new ArgumentNullException(nameof(destinatarios));
        _pushService = pushService ?? throw new ArgumentNullException(nameof(pushService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Consume(ConsumeContext<RequisicionAprobadaEvent> context)
    {
        var msg = context.Message;
        var correlationId = msg.CorrelationId != Guid.Empty
            ? msg.CorrelationId
            : (context.CorrelationId ?? Guid.Empty);

        using var logScope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        });

        if (msg.EventId == Guid.Empty || msg.RequisicionId == Guid.Empty)
        {
            _logger.LogWarning(
                "RequisicionAprobadaEvent inválido para push, se descarta. EventId {EventId}, RequisicionId {RequisicionId}, CorrelationId {CorrelationId}",
                msg.EventId, msg.RequisicionId, correlationId);
            return;
        }

        if (!await IdempotenciaConsumidorHelper.IntentarRegistrarEventoAsync(
                _db, msg.EventId, nameof(RequisicionAprobadaEvent), correlationId, _logger, context.CancellationToken))
        {
            return;
        }

        var ahora = DateTime.UtcNow;
        var tieneNumero = !string.IsNullOrWhiteSpace(msg.NumeroRequisicion);
        var comprasIds = await _destinatarios.GetUsuarioIdsPorRolesAsync(RolesCompras, context.CancellationToken);

        if (comprasIds.Count > 0)
        {
            var cuerpoCompras = tieneNumero
                ? $"La requisición {msg.NumeroRequisicion!.Trim()} está lista para licitar."
                : "Hay una nueva requisición aprobada lista para licitar.";

            await _pushService.EnviarAUsuariosAsync(
                comprasIds,
                new PayloadNotificacionPushDto(
                    Titulo: "Requisición aprobada",
                    Cuerpo: cuerpoCompras,
                    Ruta: "/compras/licitaciones",
                    Tipo: "RequisicionAprobada",
                    TimestampUtc: ahora),
                context.CancellationToken);
        }

        var candidatosCapataz = new HashSet<Guid>();
        if (msg.SolicitanteId != Guid.Empty && await _destinatarios.EsUsuarioActivoAsync(msg.SolicitanteId, context.CancellationToken))
        {
            candidatosCapataz.Add(msg.SolicitanteId);
        }

        var capatazDeObra = await _destinatarios.GetCapatazIdDeObraAsync(msg.ObraId, context.CancellationToken);
        if (capatazDeObra.HasValue)
        {
            candidatosCapataz.Add(capatazDeObra.Value);
        }

        foreach (var idCompra in comprasIds)
        {
            candidatosCapataz.Remove(idCompra);
        }

        if (candidatosCapataz.Count > 0)
        {
            var cuerpoCapataz = tieneNumero
                ? $"Tu requisición {msg.NumeroRequisicion!.Trim()} fue aprobada."
                : "Tu requisición de obra fue aprobada.";

            await _pushService.EnviarAUsuariosAsync(
                candidatosCapataz.ToList(),
                new PayloadNotificacionPushDto(
                    Titulo: "Requisición aprobada",
                    Cuerpo: cuerpoCapataz,
                    Ruta: $"/obra/requisiciones/{msg.RequisicionId}/seguimiento",
                    Tipo: "RequisicionAprobada",
                    TimestampUtc: ahora),
                context.CancellationToken);
        }
    }
}
