namespace ObraSync.Licitaciones.Application.DTOs;

public record DespacharOrdenCompraRequestDto(
    DateTime? FechaEstimadaLlegada = null,
    string? Observacion = null
);

public record OrdenCompraResponseDto(
    Guid Id,
    Guid LicitacionId,
    Guid CotizacionId,
    string Estado,
    DateTime FechaEmision,
    Guid ObraId = default,
    Guid ProveedorId = default,
    bool FueDespachada = false,
    DateTime? FechaDespacho = null,
    Guid? DespachadaPor = null,
    DateTime? DespachadaEn = null,
    DateTime? FechaEstimadaLlegada = null,
    string? Observacion = null,
    bool RecibidaCompleta = false,
    DateTime? FechaRecepcionCompleta = null,
    int? MiCalificacion = null,
    string? MiComentarioCalificacion = null,
    DateTime? FechaMiCalificacion = null,
    bool TieneProblemaAbierto = false,
    string? MotivoProblemaAbierto = null,
    DateTime? FechaProblemaAbierto = null,
    DateTime? ProblemaResueltoEn = null,
    Guid? ProblemaResueltoPor = null,
    int MensajesNoLeidos = 0
);