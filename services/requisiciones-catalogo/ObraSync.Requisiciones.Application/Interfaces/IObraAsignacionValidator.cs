using System;
using System.Threading;
using System.Threading.Tasks;

namespace ObraSync.Requisiciones.Application.Interfaces
{
    public interface IObraAsignacionValidator
    {
        Task<bool> EstaAsignadaACapatazAsync(
            Guid obraId,
            string? authorizationHeader,
            CancellationToken cancellationToken = default);
    }
}
