using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ObraSync.Requisiciones.Application.Common.Interfaces;
using ObraSync.Requisiciones.Application.DTOs;

namespace ObraSync.Requisiciones.Application.Services
{
    public class CatalogoService : ICatalogoService
    {
        private readonly IRequisicionesDbContext _context;

        public CatalogoService(IRequisicionesDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<IReadOnlyList<InsumoCatalogoDto>> ListarInsumosAsync(
            string? search = null,
            bool soloActivos = true,
            CancellationToken cancellationToken = default)
        {
            var query = _context.Insumos.AsNoTracking().AsQueryable();

            if (soloActivos)
            {
                query = query.Where(i => i.Activo);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(i => EF.Functions.Like(i.Codigo, $"%{term}%") ||
                                         EF.Functions.Like(i.Descripcion, $"%{term}%"));
            }

            var insumos = await query
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

            return insumos;
        }
    }
}
