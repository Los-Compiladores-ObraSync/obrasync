using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ObraSync.Requisiciones.Application.Common.Interfaces;
using ObraSync.Requisiciones.Application.DTOs;
using ObraSync.Requisiciones.Domain.Entities;

namespace ObraSync.Requisiciones.Application.Commands.CrearInsumo
{
    public class CrearInsumoCommandHandler : IRequestHandler<CrearInsumoCommand, InsumoCatalogoDto>
    {
        private readonly IRequisicionesDbContext _context;

        public CrearInsumoCommandHandler(IRequisicionesDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<InsumoCatalogoDto> Handle(CrearInsumoCommand request, CancellationToken cancellationToken)
        {
            var codigoNormalizado = request.Codigo.Trim().ToUpperInvariant();

            var existe = await _context.Insumos
                .AnyAsync(i => i.Codigo == codigoNormalizado, cancellationToken);

            if (existe)
            {
                throw new InvalidOperationException($"Ya existe un insumo con el código '{request.Codigo}'.");
            }

            var insumo = new Insumo(
                Guid.NewGuid(),
                request.Codigo,
                request.Descripcion,
                request.UnidadMedida,
                request.Activo
            );

            _context.Insumos.Add(insumo);
            await _context.SaveChangesAsync(cancellationToken);

            return new InsumoCatalogoDto
            {
                ArticuloId = insumo.Id,
                Codigo = insumo.Codigo,
                Descripcion = insumo.Descripcion,
                UnidadMedida = insumo.UnidadMedida,
                Activo = insumo.Activo
            };
        }
    }
}
