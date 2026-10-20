using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ObraSync.Requisiciones.Application.Interfaces;
using ObraSync.Requisiciones.Domain.Entities;

namespace ObraSync.Requisiciones.Infrastructure.Persistence.Repositories
{
    public class RequisicionRepository : IRequisicionRepository
    {
        private readonly RequisicionesDbContext _context;

        public RequisicionRepository(RequisicionesDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<Requisicion?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            return await _context.Requisiciones
                .Include(r => r.Items)
                .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        }

        public async Task<IReadOnlyList<Requisicion>> GetFilteredAsync(
            Guid? obraId = null,
            string? estado = null,
            CancellationToken cancellationToken = default)
        {
            var dbQuery = _context.Requisiciones
                .AsNoTracking()
                .Include(r => r.Items)
                .AsQueryable();

            if (obraId.HasValue && obraId.Value != Guid.Empty)
            {
                dbQuery = dbQuery.Where(r => r.ObraId == obraId.Value);
            }

            if (!string.IsNullOrWhiteSpace(estado))
            {
                var estadoNormalizado = estado.Trim();
                dbQuery = dbQuery.Where(r => r.Estado.ToLower() == estadoNormalizado.ToLower());
            }

            return await dbQuery
                .OrderByDescending(r => r.FechaCreacion)
                .ToListAsync(cancellationToken);
        }

        public async Task<int> GetNextSequenceNumberAsync(
            CancellationToken cancellationToken = default)
        {
            var count = await _context.Requisiciones.CountAsync(cancellationToken);
            return count + 1;
        }

        public Task AddAsync(
            Requisicion requisicion,
            CancellationToken cancellationToken = default)
        {
            _context.Requisiciones.Add(requisicion);
            return Task.CompletedTask;
        }

        public async Task SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
