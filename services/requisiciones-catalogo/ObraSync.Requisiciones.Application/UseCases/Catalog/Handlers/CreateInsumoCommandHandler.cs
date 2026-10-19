using System;
using System.Threading;
using System.Threading.Tasks;
using ObraSync.Requisiciones.Application.DTOs;
using ObraSync.Requisiciones.Application.Interfaces;
using ObraSync.Requisiciones.Application.Mappings;
using ObraSync.Requisiciones.Application.UseCases.Catalog.Commands;
using ObraSync.Requisiciones.Domain.Entities;

namespace ObraSync.Requisiciones.Application.UseCases.Catalog.Handlers
{
    public class CreateInsumoCommandHandler
    {
        private readonly IInsumoRepository _repository;

        public CreateInsumoCommandHandler(IInsumoRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public async Task<InsumoCatalogoDto> Handle(
            CreateInsumoCommand command,
            CancellationToken cancellationToken = default)
        {
            var codigoNormalizado = command.Codigo?.Trim().ToUpperInvariant() ?? string.Empty;

            if (!string.IsNullOrEmpty(codigoNormalizado))
            {
                var existe = await _repository.ExistsByCodigoAsync(codigoNormalizado, cancellationToken);
                if (existe)
                {
                    throw new InvalidOperationException($"Ya existe un insumo con el código '{command.Codigo}'.");
                }
            }

            var insumo = new Insumo(
                Guid.NewGuid(),
                command.Codigo ?? string.Empty,
                command.Descripcion ?? string.Empty,
                command.UnidadMedida ?? string.Empty,
                command.Activo,
                command.PrecioReferencia,
                command.Moneda
            );

            await _repository.AddAsync(insumo, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            return insumo.ToDto();
        }
    }
}
