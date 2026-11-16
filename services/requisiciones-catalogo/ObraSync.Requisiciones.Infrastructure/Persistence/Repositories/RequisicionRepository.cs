using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ObraSync.Requisiciones.Application.DTOs;
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
                .Include(r => r.HistorialRechazos)
                .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        }

        public async Task<Requisicion?> GetByIdempotencyKeyAsync(
            Guid solicitanteId,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey))
                return null;

            var keyNormalizada = idempotencyKey.Trim();
            return await _context.Requisiciones
                .AsNoTracking()
                .Include(r => r.Items)
                .Include(r => r.HistorialRechazos)
                .FirstOrDefaultAsync(
                    r => r.SolicitanteId == solicitanteId && r.IdempotencyKey == keyNormalizada,
                    cancellationToken);
        }

        public async Task<IReadOnlyList<Requisicion>> GetRecentBySolicitanteAndObraAsync(
            Guid solicitanteId,
            Guid obraId,
            DateTime desdeUtc,
            CancellationToken cancellationToken = default)
        {
            var limiteUtc = desdeUtc.ToUniversalTime();
            return await _context.Requisiciones
                .AsNoTracking()
                .Include(r => r.Items)
                .Include(r => r.HistorialRechazos)
                .Where(r =>
                    r.SolicitanteId == solicitanteId &&
                    r.ObraId == obraId &&
                    r.FechaCreacion >= limiteUtc)
                .OrderByDescending(r => r.FechaCreacion)
                .ToListAsync(cancellationToken);
        }

        public async Task<PaginaDe<Requisicion>> GetFilteredAsync(
            Guid? obraId = null,
            string? estado = null,
            Guid? solicitanteId = null,
            ParametrosPagina? paginacion = null,
            CancellationToken cancellationToken = default)
        {
            var p = paginacion ?? new ParametrosPagina();

            var consultaFiltros = _context.Requisiciones
                .AsNoTracking()
                .AsQueryable();

            if (obraId.HasValue && obraId.Value != Guid.Empty)
            {
                consultaFiltros = consultaFiltros.Where(r => r.ObraId == obraId.Value);
            }

            if (!string.IsNullOrWhiteSpace(estado))
            {
                var estadoNormalizado = estado.Trim();
                consultaFiltros = consultaFiltros.Where(r => r.Estado.ToLower() == estadoNormalizado.ToLower());
            }

            if (solicitanteId.HasValue && solicitanteId.Value != Guid.Empty)
            {
                consultaFiltros = consultaFiltros.Where(r => r.SolicitanteId == solicitanteId.Value);
            }

            var total = await consultaFiltros.CountAsync(cancellationToken);

            var items = await consultaFiltros
                .Include(r => r.Items)
                .Include(r => r.HistorialRechazos)
                .OrderByDescending(r => r.FechaCreacion)
                .ThenBy(r => r.Id)
                .Skip(p.Saltear)
                .Take(p.Tamano)
                .ToListAsync(cancellationToken);

            return new PaginaDe<Requisicion>(items, total);
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
