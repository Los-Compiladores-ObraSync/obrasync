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
    public class InsumoRepository : IInsumoRepository
    {
        private readonly RequisicionesDbContext _context;

        public InsumoRepository(RequisicionesDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<IReadOnlyList<Insumo>> GetFilteredAsync(
            string? search = null,
            bool soloActivos = true,
            CancellationToken cancellationToken = default)
        {
            var dbQuery = _context.Insumos.AsNoTracking().AsQueryable();

            if (soloActivos)
            {
                dbQuery = dbQuery.Where(i => i.Activo);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                dbQuery = dbQuery.Where(i => EF.Functions.Like(i.Codigo, $"%{term}%") ||
                                             EF.Functions.Like(i.Descripcion, $"%{term}%"));
            }

            return await dbQuery
                .OrderBy(i => i.Codigo)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<Insumo>> GetByIdsAsync(
            IEnumerable<Guid> ids,
            CancellationToken cancellationToken = default)
        {
            var listaIds = ids.Distinct().ToList();
            return await _context.Insumos
                .AsNoTracking()
                .Where(i => listaIds.Contains(i.Id))
                .ToListAsync(cancellationToken);
        }

        public async Task<bool> ExistsByCodigoAsync(
            string codigoNormalizado,
            CancellationToken cancellationToken = default)
        {
            return await _context.Insumos
                .AnyAsync(i => i.Codigo == codigoNormalizado, cancellationToken);
        }

        public Task AddAsync(
            Insumo insumo,
            CancellationToken cancellationToken = default)
        {
            _context.Insumos.Add(insumo);
            return Task.CompletedTask;
        }

        public async Task SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
