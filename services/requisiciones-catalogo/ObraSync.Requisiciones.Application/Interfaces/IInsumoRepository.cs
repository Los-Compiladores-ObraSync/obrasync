using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ObraSync.Requisiciones.Domain.Entities;

namespace ObraSync.Requisiciones.Application.Interfaces
{
    public interface IInsumoRepository
    {
        Task<IReadOnlyList<Insumo>> GetFilteredAsync(
            string? search = null,
            bool soloActivos = true,
            CancellationToken cancellationToken = default);

        Task<bool> ExistsByCodigoAsync(
            string codigoNormalizado,
            CancellationToken cancellationToken = default);

        Task AddAsync(
            Insumo insumo,
            CancellationToken cancellationToken = default);

        Task SaveChangesAsync(
            CancellationToken cancellationToken = default);
    }
}
