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

    // Control de estado de la orden de compra (#94, #95, #96, #154)
    public EstadoOrdenCompra Estado { get; set; } = EstadoOrdenCompra.Activa;
    public string? MotivoBloqueo { get; set; }

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
        DateTime? fechaDespacho = null)
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

        var oc = new OrdenCompra
        {
            Id = id,
            ObraId = obraId,
            ProveedorId = proveedorId,
            MontoTotal = montoTotal,
            Etapa = string.IsNullOrWhiteSpace(etapa) ? null : etapa.Trim(),
            Estado = estado,
            MotivoBloqueo = string.IsNullOrWhiteSpace(motivoBloqueo) ? null : motivoBloqueo.Trim(),
            SinPartidaPresupuestaria = sinPartidaPresupuestaria,
            FechaDespacho = fechaDespacho.HasValue ? DateTime.SpecifyKind(fechaDespacho.Value, DateTimeKind.Utc) : null,
            CreatedAt = DateTime.UtcNow
        };

        foreach (var item in listaItems)
        {
            item.OrdenCompraId = id;
            oc._items.Add(item);
        }

        return oc;
    }

    /// <summary>
    /// Bloquea preventivamente la orden de compra al superar la partida presupuestaria de la etapa (#94, #95).
    /// </summary>
    public void BloquearPorDesvioPresupuestario(string? motivo = null)
    {
        Estado = EstadoOrdenCompra.Bloqueada;
        MotivoBloqueo = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();
        UpdatedAt = DateTime.UtcNow;
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
        FechaDespacho = DateTime.SpecifyKind(fechaDespachoUtc, DateTimeKind.Utc);
        UpdatedAt = ahora;
    }
}
