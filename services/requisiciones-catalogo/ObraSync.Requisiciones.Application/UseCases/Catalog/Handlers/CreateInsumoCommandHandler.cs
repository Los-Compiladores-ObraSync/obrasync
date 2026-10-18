using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ObraSync.Requisiciones.Application.DTOs;
using ObraSync.Requisiciones.Application.Interfaces;
using ObraSync.Requisiciones.Application.Mappings;
using ObraSync.Requisiciones.Application.UseCases.Catalog.Commands;
using ObraSync.Requisiciones.Domain.Entities;

namespace ObraSync.Requisiciones.Application.UseCases.Catalog.Handlers
{
    public class CreateInsumoCommandHandler
    {
        private readonly IRequisicionesDbContext _context;

        public CreateInsumoCommandHandler(IRequisicionesDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<InsumoCatalogoDto> Handle(
            CreateInsumoCommand command,
            CancellationToken cancellationToken = default)
        {
            var codigoNormalizado = command.Codigo.Trim().ToUpperInvariant();

            var existe = await _context.Insumos
                .AnyAsync(i => i.Codigo == codigoNormalizado, cancellationToken);

            if (existe)
            {
                throw new InvalidOperationException($"Ya existe un insumo con el código '{command.Codigo}'.");
            }

            var insumo = new Insumo(
                Guid.NewGuid(),
                command.Codigo,
                command.Descripcion,
                command.UnidadMedida,
                command.Activo
            );

            _context.Insumos.Add(insumo);
            await _context.SaveChangesAsync(cancellationToken);

            return insumo.ToDto();
        }
    }
}
