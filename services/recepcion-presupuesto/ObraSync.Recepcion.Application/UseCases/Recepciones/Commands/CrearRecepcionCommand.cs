using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Domain.Enums;

namespace ObraSync.Recepcion.Application.UseCases.Recepciones.Commands;

public record CrearRecepcionCommand(
    Guid ObraId,
    Guid OrdenCompraId,
    Guid UsuarioId,
    string NumeroRemito,
    DateTime FechaRecepcion,
    string? Observaciones,
    IReadOnlyList<CrearDetalleRecepcionRequest> Detalles,
    Guid CorrelationId = default,
    EstadoRecepcion? Estado = null,
    string? MotivoProblema = null,
    IReadOnlyList<FotoEvidenciaEntrada>? Fotos = null);
