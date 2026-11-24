using MassTransit;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ObraSync.Contracts.Events;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Domain.Presupuestos;
using ObraSync.Recepcion.Infrastructure.Configuration;
using ObraSync.Recepcion.Infrastructure.Persistence;

namespace ObraSync.Recepcion.Infrastructure.Consumers;

public class OrdenCompraEmitidaConsumer : IConsumer<OrdenCompraEmitidaEvent>
{
    private readonly RecepcionDbContext _db;
    private readonly IOrdenCompraRepository _ordenCompraRepository;
    private readonly ILogger<OrdenCompraEmitidaConsumer> _logger;
    private readonly decimal _montoSemillaPorEtapa;

    public OrdenCompraEmitidaConsumer(
        RecepcionDbContext db,
        IOrdenCompraRepository ordenCompraRepository,
        ILogger<OrdenCompraEmitidaConsumer> logger,
        IOptions<PresupuestoSemillaOptions>? presupuestoSemilla = null)
    {
        _db = db;
        _ordenCompraRepository = ordenCompraRepository;
        _logger = logger;
        _montoSemillaPorEtapa = presupuestoSemilla?.Value.MontoSemillaPorEtapa ?? 0m;
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
                "OrdenCompraEmitidaEvent con payload inválido, se descarta. EventId: {EventId}, OrdenCompraId: {OrdenCompraId}, CorrelationId: {CorrelationId}",
                msg.EventId, msg.OrdenCompraId, correlationId);
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
                "OrdenCompraEmitidaEvent contiene ArticuloId duplicados, se descarta. EventId: {EventId}, OrdenCompraId: {OrdenCompraId}, CorrelationId: {CorrelationId}, Artículos: {Articulos}",
                msg.EventId, msg.OrdenCompraId, correlationId, string.Join(", ", duplicadosEnPayload));
            return;
        }

        // ── PASO 3: Idempotencia nivel 1 (Evento ya procesado) ──────────────
        if (await _db.EventosProcesados.AnyAsync(e => e.EventId == msg.EventId, context.CancellationToken))
        {
            _logger.LogInformation("Evento {EventId} duplicado, se descarta. CorrelationId: {CorrelationId}", msg.EventId, correlationId);
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
                    items,
                    msg.Etapa);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "OrdenCompra {OrdenCompraId} rechazada por regla de dominio, se descarta. CorrelationId: {CorrelationId}", msg.OrdenCompraId, correlationId);
                return;
            }

            // ── CONTROL PRESUPUESTARIO Y EMPAREJAMIENTO DE ETAPA (#154, #94, #95, #195, #196) ──
            var partidasObra = await _db.PartidasPresupuestarias
                .Where(p => p.ObraId == msg.ObraId)
                .ToListAsync(context.CancellationToken);

            var partida = EmparejadorPartidaPresupuestaria.BuscarPartida(partidasObra, msg.Etapa);

            // Presupuesto semilla: si la obra todavía no tiene partida para esta etapa (ni una general) y el equipo
            // configuró un monto semilla, se crea la partida acá mismo y la orden se evalúa contra ella como siempre.
            if (partida is null && _montoSemillaPorEtapa > 0)
            {
                partida = PartidaSemilla.Crear(msg.ObraId, msg.Etapa, _montoSemillaPorEtapa);
                _db.PartidasPresupuestarias.Add(partida);
                _logger.LogInformation(
                    "Partida semilla {CodigoPartida} creada para obra {ObraId} y etapa '{Etapa}' con monto {MontoAsignado}. CorrelationId: {CorrelationId}",
                    partida.CodigoPartida, msg.ObraId, msg.Etapa ?? "sin etapa", partida.MontoAsignado, correlationId);
            }

            if (partida is null)
            {
                // Regla #154 / #196: si no hay partida para la etapa ni general de obra,
                // NO se bloquea la orden, pero se marca como SinPartidaPresupuestaria
                // para visibilidad de Compras y auditoría.
                oc.MarcarSinPartida();
                _logger.LogWarning(
                    "OrdenCompra {OrdenCompraId} para obra {ObraId} y etapa '{Etapa}' no encontró partida presupuestaria (específica ni general). Queda marcada como SinPartidaPresupuestaria. CorrelationId: {CorrelationId}",
                    msg.OrdenCompraId, msg.ObraId, msg.Etapa ?? "sin etapa", correlationId);
            }
            else
            {
                oc.SinPartidaPresupuestaria = false;
                var evaluacion = EvaluadorDesvioPresupuestario.Evaluar(partida, oc.MontoTotal);

                if (evaluacion.DebeBloquearse)
                {
                    var motivo = evaluacion.Motivo.Length > 500
                        ? evaluacion.Motivo[..500]
                        : evaluacion.Motivo;

                    oc.BloquearPorDesvioPresupuestario(motivo);

                    var idCorrelacionBloqueo = correlationId != Guid.Empty
                        ? correlationId
                        : Guid.NewGuid();

                    var eventoBloqueo = new OrdenCompraBloqueadaEvent(
                        EventId: Guid.NewGuid(),
                        OccurredAt: DateTime.UtcNow,
                        CorrelationId: idCorrelacionBloqueo,
                        OrdenCompraId: oc.Id,
                        ObraId: oc.ObraId,
                        ProveedorId: oc.ProveedorId,
                        PartidaId: partida.Id,
                        Etapa: oc.Etapa,
                        MontoOrdenCompra: oc.MontoTotal,
                        MontoAsignadoPartida: evaluacion.MontoAsignado,
                        MontoComprometidoActual: evaluacion.GastoAcumulado,
                        MontoProyectado: evaluacion.MontoProyectado,
                        Exceso: evaluacion.Exceso,
                        Motivo: motivo);

                    await context.Publish(eventoBloqueo, context.CancellationToken);

                    _logger.LogWarning(
                        "OrdenCompra {OrdenCompraId} bloqueada por desvío presupuestario en partida {PartidaId} (Código: {CodigoPartida}). ObraId: {ObraId}, Etapa: {Etapa}, MontoOC: {MontoOC}, MontoAsignado: {MontoAsignado}, MontoComprometidoActual: {MontoComprometidoActual}, Exceso: {Exceso}. CorrelationId: {CorrelationId}. {Motivo}",
                        oc.Id,
                        partida.Id,
                        partida.CodigoPartida,
                        oc.ObraId,
                        oc.Etapa ?? "sin etapa",
                        oc.MontoTotal,
                        evaluacion.MontoAsignado,
                        evaluacion.GastoAcumulado,
                        evaluacion.Exceso,
                        idCorrelacionBloqueo,
                        motivo);
                }
                else
                {
                    partida.ComprometerMonto(oc.MontoTotal);
                    _logger.LogInformation(
                        "Presupuesto comprometido en partida {PartidaId} (Código: {CodigoPartida}) por {MontoTotal} para OrdenCompra {OrdenCompraId}. CorrelationId: {CorrelationId}",
                        partida.Id, partida.CodigoPartida, oc.MontoTotal, oc.Id, correlationId);
                }
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
                    "OrdenCompra {OrdenCompraId} ya existía y es idéntica al Evento {EventId}. Se registra solo el evento procesado. CorrelationId: {CorrelationId}",
                    msg.OrdenCompraId, msg.EventId, correlationId);
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
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(
                ex,
                "Conflicto de concurrencia al comprometer la partida presupuestaria para OrdenCompra {OrdenCompraId} (Evento {EventId}). Se relanza para reintentar con el saldo actualizado (#195).",
                msg.OrdenCompraId, msg.EventId);
            throw;
        }
        catch (DbUpdateException ex) when (EsViolacionDeIndiceUnico(ex))
        {
            // Un índice único puede saltar por dos motivos distintos:
            //  a) el mensaje ya se había procesado (OC o evento duplicado): se descarta, no hay nada que hacer;
            //  b) otra OC de la misma obra y etapa creó la partida semilla justo antes que esta: la OC NO se guardó,
            //     así que hay que relanzar para que MassTransit reintente y esta vez encuentre la partida.
            // Se limpia el ChangeTracker para que la consulta de abajo no vea las entidades que no se pudieron guardar.
            _db.ChangeTracker.Clear();

            var yaProcesado =
                await _db.EventosProcesados.AnyAsync(e => e.EventId == msg.EventId, context.CancellationToken)
                || await _ordenCompraRepository.ExisteAsync(msg.OrdenCompraId, context.CancellationToken);

            if (!yaProcesado)
            {
                _logger.LogWarning(
                    ex,
                    "Violación de índice único al guardar OrdenCompra {OrdenCompraId} (Evento {EventId}) sin que el mensaje estuviera procesado. Se relanza para reintentar. CorrelationId: {CorrelationId}",
                    msg.OrdenCompraId, msg.EventId, correlationId);
                throw;
            }

            _logger.LogInformation(
                "OrdenCompra {OrdenCompraId} o Evento {EventId} duplicado (detectado por índice único en BD), se descarta. CorrelationId: {CorrelationId}",
                msg.OrdenCompraId, msg.EventId, correlationId);
            return;
        }
    }

    /// <summary>2601 / 2627 son los códigos de SQL Server para clave duplicada. Es virtual para poder probar el flujo sin una base real.</summary>
    protected virtual bool EsViolacionDeIndiceUnico(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };

    private static List<string> CompararOrdenCompraConEvento(OrdenCompra existente, OrdenCompraEmitidaEvent evento)
    {
        var diferencias = new List<string>();

        if (existente.MontoTotal != evento.MontoTotal)
        {
            diferencias.Add($"MontoTotal difiere (existente: {existente.MontoTotal}, evento: {evento.MontoTotal})");
        }

        // La etapa opcional se compara normalizada en un único lugar (#196)
        if (!NormalizadorEtapa.SonEquivalentes(existente.Etapa, evento.Etapa))
        {
            diferencias.Add($"Etapa difiere (existente: {existente.Etapa ?? "sin etapa"}, evento: {evento.Etapa ?? "sin etapa"})");
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
