using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using ObraSync.Requisiciones.Domain.Constants;

namespace ObraSync.Requisiciones.Domain.Entities
{
    public class Requisicion
    {
        public const int LongitudMaximaIdempotencyKey = 100;

        public Guid Id { get; set; } = Guid.NewGuid();
        public string NumeroRequisicion { get; set; } = string.Empty;
        public Guid ObraId { get; set; }
        public string? Etapa { get; set; }
        public Guid SolicitanteId { get; set; }
        public Guid? AprobadorId { get; set; }
        public Guid? RechazadorId { get; set; }
        public string Estado { get; set; } = EstadoRequisicion.Pendiente;
        public string? Observaciones { get; set; }
        public string? MotivoRechazo { get; set; }
        public string? IdempotencyKey { get; set; }
        public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
        public DateTime? FechaAprobacion { get; set; }
        public DateTime? FechaRechazo { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public List<RequisicionItem> Items { get; set; } = new();
        public List<HistorialRechazoRequisicion> HistorialRechazos { get; set; } = new();

        public Requisicion() { }

        public Requisicion(
            Guid id,
            string numeroRequisicion,
            Guid obraId,
            Guid solicitanteId,
            IEnumerable<RequisicionItem> items,
            string? etapa = null,
            string? observaciones = null,
            DateTime? fechaCreacion = null,
            string? idempotencyKey = null)
        {
            if (string.IsNullOrWhiteSpace(numeroRequisicion))
                throw new ArgumentException("El número de requisición no puede estar vacío.", nameof(numeroRequisicion));

            if (obraId == Guid.Empty)
                throw new ArgumentException("El identificador de la obra (ObraId) es obligatorio.", nameof(obraId));

            if (solicitanteId == Guid.Empty)
                throw new ArgumentException("El identificador del solicitante (SolicitanteId) es obligatorio.", nameof(solicitanteId));

            var listaItems = items?.ToList() ?? new List<RequisicionItem>();
            if (listaItems.Count == 0)
                throw new ArgumentException("La requisición debe contener al menos un ítem.", nameof(items));

            var keyNormalizada = NormalizarIdempotencyKey(idempotencyKey);

            Id = id == Guid.Empty ? Guid.NewGuid() : id;
            NumeroRequisicion = numeroRequisicion.Trim().ToUpperInvariant();
            ObraId = obraId;
            Etapa = string.IsNullOrWhiteSpace(etapa) ? null : etapa.Trim();
            SolicitanteId = solicitanteId;
            Estado = EstadoRequisicion.Pendiente;
            Observaciones = string.IsNullOrWhiteSpace(observaciones) ? null : observaciones.Trim();
            IdempotencyKey = keyNormalizada;
            FechaCreacion = fechaCreacion?.ToUniversalTime() ?? DateTime.UtcNow;

            foreach (var item in listaItems)
            {
                item.RequisicionId = Id;
                Items.Add(item);
            }
        }

        public bool CoincideConSolicitud(
            Guid solicitanteId,
            Guid obraId,
            string? etapa,
            IEnumerable<(Guid ArticuloId, decimal Cantidad)> itemsSolicitados)
        {
            if (SolicitanteId != solicitanteId || ObraId != obraId)
                return false;

            var etapaEsta = string.IsNullOrWhiteSpace(Etapa) ? null : Etapa.Trim();
            var etapaOtra = string.IsNullOrWhiteSpace(etapa) ? null : etapa.Trim();

            if (!string.Equals(etapaEsta, etapaOtra, StringComparison.OrdinalIgnoreCase))
                return false;

            var listaSolicitada = itemsSolicitados?.ToList() ?? new List<(Guid ArticuloId, decimal Cantidad)>();
            if (Items.Count != listaSolicitada.Count)
                return false;

            var actualesOrdenados = Items
                .OrderBy(i => i.ArticuloId)
                .ThenBy(i => i.Cantidad)
                .Select(i => (i.ArticuloId, i.Cantidad))
                .ToList();

            var solicitadosOrdenados = listaSolicitada
                .OrderBy(i => i.ArticuloId)
                .ThenBy(i => i.Cantidad)
                .ToList();

            for (var i = 0; i < actualesOrdenados.Count; i++)
            {
                if (actualesOrdenados[i].ArticuloId != solicitadosOrdenados[i].ArticuloId ||
                    actualesOrdenados[i].Cantidad != solicitadosOrdenados[i].Cantidad)
                {
                    return false;
                }
            }

            return true;
        }

        public void Aprobar(Guid aprobadorId, DateTime? fechaAprobacion = null)
        {
            if (aprobadorId == Guid.Empty)
                throw new ArgumentException("El identificador del aprobador es obligatorio.", nameof(aprobadorId));

            if (!string.Equals(Estado, EstadoRequisicion.Pendiente, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"No se puede aprobar una requisición en estado '{Estado}'. Solo se pueden aprobar requisiciones pendientes.");

            Estado = EstadoRequisicion.Aprobada;
            AprobadorId = aprobadorId;
            FechaAprobacion = fechaAprobacion?.ToUniversalTime() ?? DateTime.UtcNow;
        }

        public void Rechazar(
            Guid rechazadorId,
            string motivo,
            DateTime? fechaRechazo = null,
            string? categoriaMotivo = null,
            string? comentario = null)
        {
            if (rechazadorId == Guid.Empty)
                throw new ArgumentException("El identificador del usuario que rechaza es obligatorio.", nameof(rechazadorId));

            var motivoLimpio = MotivosRechazoRequisicion.ResolverYValidar(motivo, categoriaMotivo, comentario);

            if (!string.Equals(Estado, EstadoRequisicion.Pendiente, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"No se puede rechazar una requisición en estado '{Estado}'. Solo se pueden rechazar requisiciones pendientes.");

            var fechaEfectiva = fechaRechazo?.ToUniversalTime() ?? DateTime.UtcNow;

            Estado = EstadoRequisicion.Rechazada;
            RechazadorId = rechazadorId;
            MotivoRechazo = motivoLimpio;
            FechaRechazo = fechaEfectiva;

            HistorialRechazos.Add(new HistorialRechazoRequisicion(
                Guid.NewGuid(),
                Id,
                rechazadorId,
                motivoLimpio,
                fechaEfectiva));
        }

        /// <summary>
        /// Anula una requisición que todavía no se aprobó (Pendiente). Una aprobada ya pasó a Licitaciones
        /// y una rechazada ya está cerrada para Compras: en esos estados no se puede cancelar.
        /// </summary>
        public void Cancelar()
        {
            if (!string.Equals(Estado, EstadoRequisicion.Pendiente, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Solo se pueden cancelar requisiciones pendientes. Estado actual: '{Estado}'.");

            Estado = EstadoRequisicion.Cancelada;
        }

        public static string? NormalizarIdempotencyKey(string? idempotencyKey)
        {
            var normalizada = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
            if (normalizada != null && normalizada.Length > LongitudMaximaIdempotencyKey)
                throw new ArgumentException(
                    $"El encabezado Idempotency-Key no puede superar los {LongitudMaximaIdempotencyKey} caracteres.",
                    nameof(idempotencyKey));

            return normalizada;
        }

        public static string FormatearNumero(int anio, int secuencia)
        {
            if (secuencia <= 0)
                throw new ArgumentOutOfRangeException(nameof(secuencia), "La secuencia de requisición debe ser mayor a cero.");

            return $"REQ-{anio}-{secuencia:D4}";
        }

        public void AsegurarPuedeSerModificadaPor(Guid usuarioId, bool esAdmin, string accion)
        {
            if (!esAdmin && SolicitanteId != usuarioId)
            {
                throw new UnauthorizedAccessException(
                    $"Solo el capataz solicitante dueño de la requisición o un administrador pueden {accion}.");
            }
        }

        public void AsegurarPuedeEditarse()
        {
            if (!string.Equals(Estado, EstadoRequisicion.Rechazada, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Estado, EstadoRequisicion.Pendiente, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Solo se pueden editar requisiciones en estado '{EstadoRequisicion.Pendiente}' o '{EstadoRequisicion.Rechazada}'. Estado actual: '{Estado}'.");
            }
        }

        /// <summary>
        /// Edita los datos de la requisición. Sirve para una Rechazada (se corrige y vuelve a Pendiente, y queda el historial
        /// de rechazos) y para una Pendiente (se corrige antes de que Compras la apruebe; sigue Pendiente).
        /// </summary>
        public void Reenviar(
            IEnumerable<RequisicionItem> nuevosItems,
            string? etapa = null,
            string? observaciones = null)
        {
            AsegurarPuedeEditarse();

            var listaItems = nuevosItems?.ToList() ?? new List<RequisicionItem>();
            if (listaItems.Count == 0)
            {
                throw new ArgumentException("La requisición debe contener al menos un ítem.", nameof(nuevosItems));
            }

            // Si la requisición tenía un rechazo cargado en cabecera que no estuviera aún en HistorialRechazos, lo asentamos
            if (!string.IsNullOrWhiteSpace(MotivoRechazo))
            {
                var motivoActual = MotivoRechazo.Trim();
                var fechaActual = FechaRechazo ?? DateTime.UtcNow;
                var yaRegistrado = HistorialRechazos.Any(h =>
                    string.Equals(h.Motivo, motivoActual, StringComparison.Ordinal) &&
                    h.RechazadorId == RechazadorId &&
                    h.FechaRechazo == fechaActual);

                if (!yaRegistrado)
                {
                    HistorialRechazos.Add(new HistorialRechazoRequisicion(
                        Guid.NewGuid(),
                        Id,
                        RechazadorId,
                        motivoActual,
                        fechaActual));
                }
            }

            Etapa = string.IsNullOrWhiteSpace(etapa) ? null : etapa.Trim();
            Observaciones = string.IsNullOrWhiteSpace(observaciones) ? null : observaciones.Trim();

            Items.Clear();
            foreach (var item in listaItems)
            {
                item.RequisicionId = Id;
                Items.Add(item);
            }

            // Volvemos a Pendiente limpiando el rechazo activo de la cabecera (ya quedó en HistorialRechazos)
            Estado = EstadoRequisicion.Pendiente;
            RechazadorId = null;
            MotivoRechazo = null;
            FechaRechazo = null;
            AprobadorId = null;
            FechaAprobacion = null;
        }
    }
}
