using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.Mappings;

public static class OrdenCompraMappingExtensions
{
    public static OrdenCompraResponseDto ToDto(
        this OrdenCompra ordenCompra,
        CalificacionProveedor? miCalificacion = null,
        int mensajesNoLeidos = 0)
    {
        ArgumentNullException.ThrowIfNull(ordenCompra);

        var fechaEmisionUtc = NormalizarAUtc(ordenCompra.FechaEmision);

        DateTime? fechaDespachoUtc = NormalizarNullableAUtc(ordenCompra.FechaDespacho);
        DateTime? fechaEstimadaLlegadaUtc = NormalizarNullableAUtc(ordenCompra.FechaEstimadaLlegada);
        DateTime? fechaRecepcionCompletaUtc = NormalizarNullableAUtc(ordenCompra.FechaRecepcionCompleta);
        DateTime? fechaMiCalificacionUtc = miCalificacion is not null
            ? NormalizarAUtc(miCalificacion.FechaActualizacion)
            : null;

        return new OrdenCompraResponseDto(
            Id: ordenCompra.OrdenCompraId,
            LicitacionId: ordenCompra.LicitacionId,
            CotizacionId: ordenCompra.CotizacionId,
            Estado: ordenCompra.Estado,
            FechaEmision: fechaEmisionUtc,
            ObraId: ordenCompra.ObraId,
            ProveedorId: ordenCompra.ProveedorId,
            FueDespachada: ordenCompra.FueDespachada,
            FechaDespacho: fechaDespachoUtc,
            DespachadaPor: ordenCompra.DespachadaPor,
            DespachadaEn: fechaDespachoUtc,
            FechaEstimadaLlegada: fechaEstimadaLlegadaUtc,
            Observacion: ordenCompra.Observacion,
            RecibidaCompleta: ordenCompra.RecibidaCompleta,
            FechaRecepcionCompleta: fechaRecepcionCompletaUtc,
            MiCalificacion: miCalificacion?.Calificacion,
            MiComentarioCalificacion: miCalificacion?.Comentario,
            FechaMiCalificacion: fechaMiCalificacionUtc,
            TieneProblemaAbierto: ordenCompra.TieneProblemaAbierto,
            MotivoProblemaAbierto: ordenCompra.MotivoProblemaAbierto,
            FechaProblemaAbierto: NormalizarNullableAUtc(ordenCompra.FechaProblemaAbierto),
            ProblemaResueltoEn: NormalizarNullableAUtc(ordenCompra.ProblemaResueltoEn),
            ProblemaResueltoPor: ordenCompra.ProblemaResueltoPor,
            MensajesNoLeidos: Math.Max(0, mensajesNoLeidos),
            NumeroDespacho: ordenCompra.NumeroDespacho,
            PuedeRedespachar: ordenCompra.PuedeRedespachar,
            TieneRecepcionParcialPendiente: ordenCompra.TieneRecepcionParcialPendiente,
            FechaUltimaRecepcionParcial: NormalizarNullableAUtc(ordenCompra.FechaUltimaRecepcionParcial));
    }

    public static MensajeChatOrdenDto ToDto(this MensajeChatOrden mensaje)
    {
        ArgumentNullException.ThrowIfNull(mensaje);

        return new MensajeChatOrdenDto(
            Id: mensaje.Id,
            OrdenCompraId: mensaje.OrdenCompraId,
            AutorId: mensaje.AutorId,
            AutorNombre: mensaje.AutorNombre,
            AutorRol: mensaje.AutorRol,
            EsSistema: mensaje.EsSistema,
            Texto: mensaje.Texto,
            FechaCreacion: NormalizarAUtc(mensaje.FechaCreacion));
    }

    public static LecturaChatResponseDto ToDto(this LecturaChatOrden lectura)
    {
        ArgumentNullException.ThrowIfNull(lectura);

        return new LecturaChatResponseDto(
            OrdenCompraId: lectura.OrdenCompraId,
            UsuarioId: lectura.UsuarioId,
            UltimaLecturaEn: NormalizarAUtc(lectura.UltimaLecturaEn));
    }

    public static ProblemaOrdenEstadoDto ToProblemaEstadoDto(this OrdenCompra ordenCompra)
    {
        ArgumentNullException.ThrowIfNull(ordenCompra);

        return new ProblemaOrdenEstadoDto(
            OrdenCompraId: ordenCompra.OrdenCompraId,
            TieneProblemaAbierto: ordenCompra.TieneProblemaAbierto,
            MotivoProblemaAbierto: ordenCompra.MotivoProblemaAbierto,
            FechaProblemaAbierto: NormalizarNullableAUtc(ordenCompra.FechaProblemaAbierto),
            ProblemaResueltoEn: NormalizarNullableAUtc(ordenCompra.ProblemaResueltoEn),
            ProblemaResueltoPor: ordenCompra.ProblemaResueltoPor);
    }

    internal static DateTime? NormalizarNullableAUtc(DateTime? fecha) =>
        fecha.HasValue ? NormalizarAUtc(fecha.Value) : null;

    internal static DateTime NormalizarAUtc(DateTime fecha) => fecha.Kind switch
    {
        DateTimeKind.Utc => fecha,
        DateTimeKind.Local => fecha.ToUniversalTime(),
        _ => DateTime.SpecifyKind(fecha, DateTimeKind.Utc)
    };
}
