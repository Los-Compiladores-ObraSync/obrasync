using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ObraSync.Requisiciones.Domain.Entities;

namespace ObraSync.Requisiciones.Application.Interfaces
{
    public interface IRequisicionRepository
    {
        Task<Requisicion?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<Requisicion>> GetFilteredAsync(
            Guid? obraId = null,
            string? estado = null,
            CancellationToken cancellationToken = default);

        Task<int> GetNextSequenceNumberAsync(
            CancellationToken cancellationToken = default);

        Task AddAsync(
            Requisicion requisicion,
            CancellationToken cancellationToken = default);

        Task SaveChangesAsync(
            CancellationToken cancellationToken = default);
    }
}
