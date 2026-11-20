namespace ObraSync.Recepcion.Application.UseCases.Recepciones.Commands;

/// <summary>
/// Comando para liquidar administrativamente una orden de compra (#92).
/// </summary>
public record LiquidarOrdenCompraCommand(
    Guid OrdenCompraId,
    Guid UsuarioId);
