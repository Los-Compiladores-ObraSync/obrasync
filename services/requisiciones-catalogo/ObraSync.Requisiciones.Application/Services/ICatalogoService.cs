using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ObraSync.Requisiciones.Application.DTOs;

namespace ObraSync.Requisiciones.Application.Services
{
    public interface ICatalogoService
    {
        Task<IReadOnlyList<InsumoCatalogoDto>> ListarInsumosAsync(string? search = null, bool soloActivos = true, CancellationToken cancellationToken = default);
    }
}
