using System;

namespace ObraSync.Requisiciones.Application.UseCases.Requisiciones.Commands
{
    /// <summary>Cancela una requisición pendiente. Puede hacerlo su dueño (Capataz) o un Admin.</summary>
    public record CancelarRequisicionCommand(Guid RequisicionId, Guid UsuarioId, bool EsAdmin);
}
