using ObraSync.Recepcion.Domain.Common;
using ObraSync.Recepcion.Domain.Enums;
using ObraSync.Recepcion.Domain.Exceptions;

namespace ObraSync.Recepcion.Domain.Entities;

public class Recepcion : BaseEntity
{
    // Referencia al microservicio de Usuarios y Obras (solo GUID, sin FK de SQL)
    public Guid ObraId { get; set; }

    // Referencia al microservicio de Licitaciones y Cotizaciones (solo GUID, sin FK de SQL)
    public Guid OrdenCompraId { get; set; }

    // Referencia al usuario (Capataz/Receptor) de Usuarios y Obras (solo GUID)
    public Guid RecepcionadoPorUsuarioId { get; set; }

    // Identificador comercial del comprobante de entrega en obra (remito)
    public string NumeroRemito { get; set; } = string.Empty;

    public DateTime FechaRecepcion { get; set; } = DateTime.UtcNow;

    public EstadoRecepcion Estado { get; set; } = EstadoRecepcion.Pendiente;

    public string? Observaciones { get; set; }

    // Relación interna (misma base de datos SQL Server: DB_Recepcion)
    public ICollection<DetalleRecepcion> Detalles { get; set; } = new List<DetalleRecepcion>();

    public void CambiarEstado(EstadoRecepcion nuevoEstado)
    {
        bool cambioPermitido = Estado switch
        {
            EstadoRecepcion.Pendiente =>
                nuevoEstado == EstadoRecepcion.Parcial ||
                nuevoEstado == EstadoRecepcion.Completa ||
                nuevoEstado == EstadoRecepcion.Rechazada,

            EstadoRecepcion.Parcial =>
                nuevoEstado == EstadoRecepcion.Parcial ||
                nuevoEstado == EstadoRecepcion.Completa,

            _ => false
        };

        if (!cambioPermitido)
        {
            throw new TransicionEstadoInvalidaException(
                $"No se permite cambiar una recepción de {Estado} a {nuevoEstado}.");
        }

        Estado = nuevoEstado;
        UpdatedAt = DateTime.UtcNow;
    }

    public EstadoRecepcion CalcularEstadoSegunCantidades()
    {
        if (Detalles.Count == 0)
        {
            throw new InvalidOperationException(
                "No se puede calcular el estado de una recepción sin detalles.");
        }

        bool todoRecibido = true;

        foreach (DetalleRecepcion detalle in Detalles)
        {
            if (detalle.CantidadEsperada <= 0)
            {
                throw new InvalidOperationException(
                    "La cantidad esperada debe ser mayor a cero.");
            }

            if (detalle.CantidadRecibida < 0)
            {
                throw new InvalidOperationException(
                    "La cantidad recibida no puede ser negativa.");
            }

            if (detalle.CantidadRecibida > detalle.CantidadEsperada)
            {
                throw new InvalidOperationException(
                    "La cantidad recibida no puede superar la cantidad esperada.");
            }

            if (detalle.CantidadRecibida < detalle.CantidadEsperada)
            {
                todoRecibido = false;
            }
        }

        if (todoRecibido)
        {
            return EstadoRecepcion.Completa;
        }

        return EstadoRecepcion.Parcial;
    }
}
