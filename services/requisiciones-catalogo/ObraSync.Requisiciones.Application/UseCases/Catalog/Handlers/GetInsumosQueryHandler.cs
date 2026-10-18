using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ObraSync.Requisiciones.Application.DTOs;
using ObraSync.Requisiciones.Application.Interfaces;
using ObraSync.Requisiciones.Application.Mappings;
using ObraSync.Requisiciones.Application.UseCases.Catalog.Queries;

namespace ObraSync.Requisiciones.Application.UseCases.Catalog.Handlers
{
    public class GetInsumosQueryHandler
    {
        private readonly IRequisicionesDbContext _context;

        public GetInsumosQueryHandler(IRequisicionesDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<IReadOnlyList<InsumoCatalogoDto>> Handle(
            GetInsumosQuery query,
            CancellationToken cancellationToken = default)
        {
            var dbQuery = _context.Insumos.AsNoTracking().AsQueryable();

            if (query.SoloActivos)
            {
                dbQuery = dbQuery.Where(i => i.Activo);
            }

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var term = query.Search.Trim();
                dbQuery = dbQuery.Where(i => EF.Functions.Like(i.Codigo, $"%{term}%") ||
                                             EF.Functions.Like(i.Descripcion, $"%{term}%"));
            }

            var insumos = await dbQuery
                .OrderBy(i => i.Codigo)
                .ToListAsync(cancellationToken);

            return insumos.Select(i => i.ToDto()).ToList();
        }
    }
}
