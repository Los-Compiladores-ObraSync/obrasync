using System;
using System.Collections.Generic;
using System.Linq;
using ObraSync.Recepcion.Domain.Common;
using ObraSync.Recepcion.Domain.Enums;
using ObraSync.Recepcion.Domain.Exceptions;

namespace ObraSync.Recepcion.Domain.Entities;

public class OrdenCompra : BaseEntity
{
    // Identificador de la orden de compra emitida (Guid del evento, ValueGeneratedNever)
    public Guid ObraId { get; set; }
    public Guid ProveedorId { get; set; }
    public decimal MontoTotal { get; set; }
    public string? Etapa { get; set; }

    // Control de estado de la orden de compra (#94, #95, #96, #154, #210)
    public EstadoOrdenCompra Estado { get; set; } = EstadoOrdenCompra.Activa;
    public string? MotivoBloqueo { get; set; }
    public DateTime? FechaBloqueo { get; private set; }

    // Auditoría de autorización al liberar una OC bloqueada (#96): quién (sub del token) y cuándo (UTC)
    public Guid? LiberadaPorUsuarioId { get; private set; }
    public DateTime? LiberadaEn { get; private set; }
    public string? MotivoLiberacion { get; private set; }

    // Visibilidad presupuestaria cuando no se encuentra partida (#196)
    public bool SinPartidaPresupuestaria { get; set; }

    // Control de despacho (#204): independiente del estado presupuestario (Activa / Bloqueada)
    public DateTime? FechaDespacho { get; private set; }
    public bool EstaDespachada => FechaDespacho.HasValue;

    public bool EstaBloqueada => Estado == EstadoOrdenCompra.Bloqueada;

    private readonly List<OrdenCompraItem> _items = new();
    public IReadOnlyCollection<OrdenCompraItem> Items => _items.AsReadOnly();

    // Constructor privado para EF Core
    private OrdenCompra() { }

    public static OrdenCompra Crear(
        Guid id,
        Guid obraId,
        Guid proveedorId,
        decimal montoTotal,
        IEnumerable<OrdenCompraItem> items,
        string? etapa = null,
        EstadoOrdenCompra estado = EstadoOrdenCompra.Activa,
        string? motivoBloqueo = null,
        bool sinPartidaPresupuestaria = false,
        DateTime? fechaDespacho = null,
        DateTime? fechaBloqueo = null)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("El identificador de la orden de compra no puede estar vacío.", nameof(id));

        if (obraId == Guid.Empty)
            throw new ArgumentException("El identificador de la obra no puede estar vacío.", nameof(obraId));

        if (proveedorId == Guid.Empty)
            throw new ArgumentException("El identificador del proveedor no puede estar vacío.", nameof(proveedorId));

        if (montoTotal < 0)
            throw new ArgumentException("El monto total no puede ser negativo.", nameof(montoTotal));

        if (items is null)
            throw new ArgumentNullException(nameof(items), "La colección de ítems no puede ser nula.");

        var listaItems = items.ToList();
        if (listaItems.Count == 0)
            throw new ArgumentException("La orden de compra debe contener al menos un ítem.", nameof(items));

        // Regla provisoria: se rechazan ítems duplicados por ArticuloId dentro de la misma OC
        var duplicados = listaItems
            .GroupBy(i => i.ArticuloId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicados.Count > 0)
            throw new ArgumentException($"La orden de compra contiene artículos duplicados: {string.Join(", ", duplicados)}.", nameof(items));

        if (!string.IsNullOrWhiteSpace(etapa) && etapa.Trim().Length > 200)
            throw new ArgumentException("La etapa no puede superar los 200 caracteres.", nameof(etapa));

        var ahora = DateTime.UtcNow;
        var fechaBloqueoNormalizada = FechaUtc.Normalizar(fechaBloqueo)
            ?? (estado == EstadoOrdenCompra.Bloqueada ? ahora : null);

        var oc = new OrdenCompra
        {
            Id = id,
            ObraId = obraId,
            ProveedorId = proveedorId,
            MontoTotal = montoTotal,
            Etapa = string.IsNullOrWhiteSpace(etapa) ? null : etapa.Trim(),
            Estado = estado,
            MotivoBloqueo = string.IsNullOrWhiteSpace(motivoBloqueo) ? null : motivoBloqueo.Trim(),
            FechaBloqueo = fechaBloqueoNormalizada,
            SinPartidaPresupuestaria = sinPartidaPresupuestaria,
            FechaDespacho = FechaUtc.Normalizar(fechaDespacho),
            CreatedAt = ahora
        };

        foreach (var item in listaItems)
        {
            item.OrdenCompraId = id;
            oc._items.Add(item);
        }

        return oc;
    }

    /// <summary>
    /// Bloquea preventivamente la orden de compra al superar la partida presupuestaria de la etapa (#94, #95, #210).
    /// </summary>
    public void BloquearPorDesvioPresupuestario(string? motivo = null)
    {
        var ahora = DateTime.UtcNow;
        Estado = EstadoOrdenCompra.Bloqueada;
        MotivoBloqueo = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();
        FechaBloqueo = ahora;
        UpdatedAt = ahora;
    }

    /// <summary>
    /// Libera la orden de compra bloqueada por autorización de un Administrador,
    /// registrando quién y cuándo la liberó (#96).
    /// </summary>
    public void Liberar(Guid usuarioAdminId, string? motivoLiberacion = null)
    {
        if (usuarioAdminId == Guid.Empty)
        {
            throw new ArgumentException("El identificador del administrador que libera la orden es obligatorio.", nameof(usuarioAdminId));
        }

        if (Estado != EstadoOrdenCompra.Bloqueada)
        {
            throw new TransicionEstadoInvalidaException(
                $"Solo se puede liberar una orden de compra que esté en estado Bloqueada (estado actual: {Estado}).");
        }

        if (!string.IsNullOrWhiteSpace(motivoLiberacion) && motivoLiberacion.Trim().Length > 500)
        {
            throw new ArgumentException("El motivo de liberación no puede superar los 500 caracteres.", nameof(motivoLiberacion));
        }

        var ahora = DateTime.UtcNow;
        Estado = EstadoOrdenCompra.Activa;
        MotivoBloqueo = null;
        LiberadaPorUsuarioId = usuarioAdminId;
        LiberadaEn = ahora;
        MotivoLiberacion = string.IsNullOrWhiteSpace(motivoLiberacion) ? null : motivoLiberacion.Trim();
        UpdatedAt = ahora;
    }

    /// <summary>
    /// Sobrecarga sin parámetros preservada para compatibilidad con pruebas existentes.
    /// </summary>
    public void Liberar()
    {
        Estado = EstadoOrdenCompra.Activa;
        MotivoBloqueo = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Marca la orden de compra como carente de partida presupuestaria (#196).
    /// Permite continuar la operatoria sin bloqueo preventivo (#154), pero señaliza
    /// la falta de partida para visibilidad de Compras y auditoría.
    /// </summary>
    public void MarcarSinPartida()
    {
        SinPartidaPresupuestaria = true;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Asocia la orden de compra con una partida presupuestaria (#196).
    /// </summary>
    public void AsignarPartida()
    {
        SinPartidaPresupuestaria = false;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Registra la fecha de despacho al consumir OrdenCompraDespachadaEvent (#204).
    /// Es independiente del estado presupuestario (Activa / Bloqueada) y conserva
    /// el primer despacho si recibe otro evento con distinto EventId.
    /// </summary>
    public void MarcarComoDespachada(DateTime fechaDespachoUtc)
    {
        if (fechaDespachoUtc == default)
        {
            throw new ArgumentException("La fecha de despacho es obligatoria.", nameof(fechaDespachoUtc));
        }

        if (FechaDespacho.HasValue)
        {
            return;
        }

        var ahora = DateTime.UtcNow;
        FechaDespacho = FechaUtc.Normalizar(fechaDespachoUtc);
        UpdatedAt = ahora;
    }

    /// <summary>
    /// Calcula los saldos pendientes de cada artículo de la orden de compra (#91).
    /// Si cantidadesRecibidasPrevias es null o no contiene algún artículo, se asume recibido previo = 0.
    /// Saldo = Math.Max(0m, item.Cantidad - recibidoPrevio).
    /// </summary>
    public IReadOnlyDictionary<Guid, decimal> CalcularSaldosPendientes(
        IReadOnlyDictionary<Guid, decimal>? cantidadesRecibidasPrevias = null)
    {
        var saldos = new Dictionary<Guid, decimal>();
        foreach (var item in _items)
        {
            decimal recibido = cantidadesRecibidasPrevias?.GetValueOrDefault(item.ArticuloId, 0m) ?? 0m;
            decimal pendiente = Math.Max(0m, item.Cantidad - recibido);
            saldos[item.ArticuloId] = pendiente;
        }
        return saldos;
    }

    /// <summary>
    /// Obtiene el saldo pendiente de un artículo puntual de la orden de compra (#91).
    /// Lanza ArgumentException si el artículo no pertenece a la orden de compra.
    /// </summary>
    public decimal ObtenerSaldoPendienteArticulo(
        Guid articuloId,
        IReadOnlyDictionary<Guid, decimal>? cantidadesRecibidasPrevias = null)
    {
        var item = _items.FirstOrDefault(i => i.ArticuloId == articuloId);
        if (item is null)
        {
            throw new ArgumentException($"El artículo {articuloId} no pertenece a la orden de compra {Id}.", nameof(articuloId));
        }

        decimal recibido = cantidadesRecibidasPrevias?.GetValueOrDefault(articuloId, 0m) ?? 0m;
        return Math.Max(0m, item.Cantidad - recibido);
    }

    /// <summary>
    /// Simula y valida una secuencia ordenada de entregas sucesivas (#91).
    /// Valida en cada paso que ninguna cantidad recibida sea negativa ni supere el saldo remanente vigente en ese eslabón.
    /// Devuelve el historial de saldos pendientes tras cada entrega sucesiva.
    /// </summary>
    public IReadOnlyList<IReadOnlyDictionary<Guid, decimal>> EncadenarEntregas(
        IEnumerable<IReadOnlyDictionary<Guid, decimal>> entregasSucesivas)
    {
        if (entregasSucesivas is null)
            throw new ArgumentNullException(nameof(entregasSucesivas));

        var historialSaldos = new List<IReadOnlyDictionary<Guid, decimal>>();
        var acumuladoRecibido = new Dictionary<Guid, decimal>();

        foreach (var item in _items)
        {
            acumuladoRecibido[item.ArticuloId] = 0m;
        }

        foreach (var entrega in entregasSucesivas)
        {
            if (entrega is null)
                throw new ArgumentNullException(nameof(entregasSucesivas), "Una entrega en la secuencia no puede ser nula.");

            foreach (var (articuloId, cantidadRecibida) in entrega)
            {
                var item = _items.FirstOrDefault(i => i.ArticuloId == articuloId);
                if (item is null)
                {
                    throw new ArgumentException($"El artículo {articuloId} no pertenece a la orden de compra.", nameof(entregasSucesivas));
                }

                if (cantidadRecibida < 0)
                {
                    throw new ArgumentException($"La cantidad recibida para el artículo {articuloId} no puede ser negativa.", nameof(entregasSucesivas));
                }

                decimal saldoVigente = item.Cantidad - acumuladoRecibido[articuloId];
                if (cantidadRecibida > saldoVigente)
                {
                    throw new InvalidOperationException($"La cantidad recibida ({cantidadRecibida}) para el artículo {articuloId} supera el saldo remanente vigente ({saldoVigente}).");
                }

                acumuladoRecibido[articuloId] += cantidadRecibida;
            }

            var saldosPaso = new Dictionary<Guid, decimal>();
            foreach (var item in _items)
            {
                saldosPaso[item.ArticuloId] = Math.Max(0m, item.Cantidad - acumuladoRecibido[item.ArticuloId]);
            }
            historialSaldos.Add(saldosPaso);
        }

        return historialSaldos;
    }
}
