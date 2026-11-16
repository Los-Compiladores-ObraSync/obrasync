using System;

namespace ObraSync.Recepcion.Application.UseCases.Recepciones.Commands;

public record LiberarOrdenCompraCommand(
    Guid OrdenCompraId,
    Guid UsuarioId,
    string? Motivo = null);
