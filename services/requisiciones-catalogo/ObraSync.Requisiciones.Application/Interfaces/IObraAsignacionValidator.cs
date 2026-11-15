using System;
using System.Threading;
using System.Threading.Tasks;

namespace ObraSync.Requisiciones.Application.Interfaces
{
    public interface IObraAsignacionValidator
    {
        /// <returns>true si la obra está asignada al Capataz; false si no lo está.</returns>
        /// <exception cref="ObraSync.Requisiciones.Application.Exceptions.ObraAsignacionNoVerificableException">
        /// Usuarios y Obras no respondió y no se pudo comprobar (no es lo mismo que "no asignada").
        /// </exception>
        Task<bool> EstaAsignadaACapatazAsync(
            Guid obraId,
            string? authorizationHeader,
            string? correlationId,
            CancellationToken cancellationToken = default);
    }
}
