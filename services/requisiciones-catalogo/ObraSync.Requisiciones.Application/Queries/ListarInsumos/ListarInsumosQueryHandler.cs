using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ObraSync.Requisiciones.Application.Common.Interfaces;
using ObraSync.Requisiciones.Application.DTOs;

namespace ObraSync.Requisiciones.Application.Queries.ListarInsumos
{
    public class ListarInsumosQueryHandler : IRequestHandler<ListarInsumosQuery, IReadOnlyList<InsumoCatalogoDto>>
    {
        private readonly IRequisicionesDbContext _context;

        public ListarInsumosQueryHandler(IRequisicionesDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<IReadOnlyList<InsumoCatalogoDto>> Handle(ListarInsumosQuery request, CancellationToken cancellationToken)
        {
            var query = _context.Insumos.AsNoTracking().AsQueryable();

            if (request.SoloActivos)
            {
                query = query.Where(i => i.Activo);
            }

            if (!string.IsNullOrWhiteSpace(request.Search))
            {
                var term = request.Search.Trim();
                query = query.Where(i => EF.Functions.Like(i.Codigo, $"%{term}%") ||
                                         EF.Functions.Like(i.Descripcion, $"%{term}%"));
            }

            return await query
                .OrderBy(i => i.Codigo)
                .Select(i => new InsumoCatalogoDto
                {
                    ArticuloId = i.Id,
                    Codigo = i.Codigo,
                    Descripcion = i.Descripcion,
                    UnidadMedida = i.UnidadMedida,
                    Activo = i.Activo
                })
                .ToListAsync(cancellationToken);
        }
    }
}
