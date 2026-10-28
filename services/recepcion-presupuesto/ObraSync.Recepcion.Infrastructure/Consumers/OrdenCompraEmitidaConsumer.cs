using MassTransit;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ObraSync.Contracts.Events;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Infrastructure.Persistence;

namespace ObraSync.Recepcion.Infrastructure.Consumers;

public class OrdenCompraEmitidaConsumer : IConsumer<OrdenCompraEmitidaEvent>
{
    private readonly RecepcionDbContext _db;
    private readonly IOrdenCompraRepository _ordenCompraRepository;
    private readonly ILogger<OrdenCompraEmitidaConsumer> _logger;

    public OrdenCompraEmitidaConsumer(
        RecepcionDbContext db,
        IOrdenCompraRepository ordenCompraRepository,
        ILogger<OrdenCompraEmitidaConsumer> logger)
    {
        _db = db;
        _ordenCompraRepository = ordenCompraRepository;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OrdenCompraEmitidaEvent> context)
    {
        var msg = context.Message;

        // ── PASO 1: Validación básica del payload ───────────────────────────
        // Nota de diseño:
        // 1. MontoTotal no se valida contra la suma de (Cantidad * PrecioUnitario) de los ítems
        //    porque puede incluir flete, recargos o impuestos globales emitidos por Licitaciones.
        // 2. Descripcion no se valida ni se trunca (se persiste en dominio tal como proviene del evento).
        if (msg.EventId == Guid.Empty ||
            msg.OrdenCompraId == Guid.Empty ||
            msg.ObraId == Guid.Empty ||
            msg.ProveedorId == Guid.Empty ||
            msg.MontoTotal < 0 ||
            msg.Items is null ||
            msg.Items.Count == 0 ||
            msg.Items.Any(i => i.ArticuloId == Guid.Empty ||
                               i.Cantidad <= 0 ||
                               i.PrecioUnitario < 0 ||
                               string.IsNullOrWhiteSpace(i.UnidadMedida)))
        {
            _logger.LogWarning(
                "OrdenCompraEmitidaEvent con payload inválido, se descarta. EventId: {EventId}, OrdenCompraId: {OrdenCompraId}",
                msg.EventId, msg.OrdenCompraId);
            return;
        }

        // ── PASO 2: Validación previa de duplicados en payload ──────────────
        var duplicadosEnPayload = msg.Items
            .GroupBy(i => i.ArticuloId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicadosEnPayload.Count > 0)
        {
            _logger.LogWarning(
                "OrdenCompraEmitidaEvent contiene ArticuloId duplicados, se descarta. EventId: {EventId}, OrdenCompraId: {OrdenCompraId}, Artículos: {Articulos}",
                msg.EventId, msg.OrdenCompraId, string.Join(", ", duplicadosEnPayload));
            return;
        }

        // ── PASO 3: Idempotencia nivel 1 (Evento ya procesado) ──────────────
        if (await _db.EventosProcesados.AnyAsync(e => e.EventId == msg.EventId, context.CancellationToken))
        {
            _logger.LogInformation("Evento {EventId} duplicado, se descarta", msg.EventId);
            return;
        }

        // ── PASO 4: Comprobación de existencia de la Orden de Compra ────────
        var ocExiste = await _ordenCompraRepository.ExisteAsync(msg.OrdenCompraId, context.CancellationToken);

        if (!ocExiste)
        {
            // ── RAMA A: OC nueva ────────────────────────────────────────────
            // Defensa en profundidad: aunque el payload ya pasó las validaciones de estructura previas,
            // se captura ArgumentException para proteger el consumidor si las invariantes de negocio de
            // OrdenCompra.Crear u OrdenCompraItem.Crear evolucionan o incorporan reglas adicionales en el dominio.
            OrdenCompra oc;
            try
            {
                var items = msg.Items.Select(i => OrdenCompraItem.Crear(
                    i.ArticuloId,
                    i.Cantidad,
                    i.UnidadMedida,
                    i.PrecioUnitario,
                    i.Descripcion)).ToList();

                oc = OrdenCompra.Crear(
                    msg.OrdenCompraId,
                    msg.ObraId,
                    msg.ProveedorId,
                    msg.MontoTotal,
                    items);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "OrdenCompra {OrdenCompraId} rechazada por regla de dominio, se descarta", msg.OrdenCompraId);
                return;
            }

            _ordenCompraRepository.Agregar(oc);

            _db.EventosProcesados.Add(new EventoProcesado
            {
                EventId = msg.EventId,
                TipoEvento = nameof(OrdenCompraEmitidaEvent),
                ProcesadoEn = DateTime.UtcNow
            });
        }
        else
        {
            // ── RAMA B: OC existente (Política 1 inmutable) ──────────────────
            var ocExistente = await _ordenCompraRepository.ObtenerPorIdAsync(msg.OrdenCompraId, context.CancellationToken)
                ?? throw new InvalidOperationException(
                    $"Inconsistencia en persistencia: la OrdenCompra {msg.OrdenCompraId} fue reportada como existente por ExisteAsync, pero ObtenerPorIdAsync devolvió null.");

            var diferencias = CompararOrdenCompraConEvento(ocExistente, msg);

            if (diferencias.Count > 0)
            {
                _logger.LogWarning(
                    "OrdenCompra {OrdenCompraId} ya existía pero difiere con Evento {EventId}. Diferencias: {Diferencias}",
                    msg.OrdenCompraId, msg.EventId, string.Join("; ", diferencias));
            }
            else
            {
                _logger.LogInformation(
                    "OrdenCompra {OrdenCompraId} ya existía y es idéntica al Evento {EventId}. Se registra solo el evento procesado.",
                    msg.OrdenCompraId, msg.EventId);
            }

            // Se agrega SOLO el evento procesado; la OC no se toca
            _db.EventosProcesados.Add(new EventoProcesado
            {
                EventId = msg.EventId,
                TipoEvento = nameof(OrdenCompraEmitidaEvent),
                ProcesadoEn = DateTime.UtcNow
            });
        }

        // ── PASO 5: Único SaveChangesAsync del flujo ────────────────────────
        try
        {
            await _db.SaveChangesAsync(context.CancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            _logger.LogInformation(
                "OrdenCompra {OrdenCompraId} o Evento {EventId} duplicado (detectado por índice único en BD), se descarta",
                msg.OrdenCompraId, msg.EventId);
            return;
        }
    }

    private static List<string> CompararOrdenCompraConEvento(OrdenCompra existente, OrdenCompraEmitidaEvent evento)
    {
        var diferencias = new List<string>();

        if (existente.MontoTotal != evento.MontoTotal)
        {
            diferencias.Add($"MontoTotal difiere (existente: {existente.MontoTotal}, evento: {evento.MontoTotal})");
        }

        if (existente.Items.Count != evento.Items.Count)
        {
            diferencias.Add($"Cantidad total de ítems difiere (existente: {existente.Items.Count}, evento: {evento.Items.Count})");
        }

        // Unión de ArticuloId de ambos lados para comparación simétrica
        var todosArticuloIds = existente.Items.Select(i => i.ArticuloId)
            .Union(evento.Items.Select(i => i.ArticuloId))
            .Distinct();

        foreach (var articuloId in todosArticuloIds)
        {
            var itemExistente = existente.Items.FirstOrDefault(i => i.ArticuloId == articuloId);
            var itemEvento = evento.Items.FirstOrDefault(i => i.ArticuloId == articuloId);

            // Regla explícita: ArticuloId presente de un solo lado es diferencia
            if (itemExistente is null)
            {
                diferencias.Add($"Artículo {articuloId} presente en evento pero ausente en OC existente");
                continue;
            }

            if (itemEvento is null)
            {
                diferencias.Add($"Artículo {articuloId} presente en OC existente pero ausente en evento");
                continue;
            }

            // Ambos lados contienen exactamente 1 ítem para este ArticuloId:
            // - En evento: garantizado por la validación previa del payload en Consume.
            // - En existente: garantizado por la regla de dominio en OrdenCompra.Crear.
            if (itemExistente.Cantidad != itemEvento.Cantidad)
            {
                diferencias.Add($"Artículo {articuloId}: Cantidad difiere (existente: {itemExistente.Cantidad}, evento: {itemEvento.Cantidad})");
            }

            if (itemExistente.PrecioUnitario != itemEvento.PrecioUnitario)
            {
                diferencias.Add($"Artículo {articuloId}: PrecioUnitario difiere (existente: {itemExistente.PrecioUnitario}, evento: {itemEvento.PrecioUnitario})");
            }
        }

        return diferencias;
    }
}
