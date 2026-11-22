namespace ObraSync.Licitaciones.Application.DTOs;

public record EnviarMensajeChatRequestDto(string? Texto);

public record MensajeChatOrdenDto(
    Guid Id,
    Guid OrdenCompraId,
    Guid? AutorId,
    string AutorNombre,
    string AutorRol,
    bool EsSistema,
    string Texto,
    DateTime FechaCreacion
);

public record LecturaChatResponseDto(
    Guid OrdenCompraId,
    Guid UsuarioId,
    DateTime UltimaLecturaEn
);

public record NoLeidosOrdenDto(
    Guid OrdenCompraId,
    int MensajesNoLeidos
);

public record ResumenNoLeidosChatDto(
    int TotalNoLeidos,
    IReadOnlyList<NoLeidosOrdenDto> Ordenes
);

public record ProblemaOrdenEstadoDto(
    Guid OrdenCompraId,
    bool TieneProblemaAbierto,
    string? MotivoProblemaAbierto,
    DateTime? FechaProblemaAbierto,
    DateTime? ProblemaResueltoEn,
    Guid? ProblemaResueltoPor
);
