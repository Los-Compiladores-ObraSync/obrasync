using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ObraSync.Requisiciones.Domain.Entities;

namespace ObraSync.Requisiciones.Application.Interfaces
{
    public interface IRequisicionesDbContext
    {
        DbSet<Insumo> Insumos { get; }

        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
