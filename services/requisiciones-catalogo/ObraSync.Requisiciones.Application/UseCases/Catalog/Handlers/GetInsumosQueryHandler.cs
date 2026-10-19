using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObraSync.Requisiciones.Application.DTOs;
using ObraSync.Requisiciones.Application.Interfaces;
using ObraSync.Requisiciones.Application.Mappings;
using ObraSync.Requisiciones.Application.UseCases.Catalog.Queries;

namespace ObraSync.Requisiciones.Application.UseCases.Catalog.Handlers
{
    public class GetInsumosQueryHandler
    {
        private readonly IInsumoRepository _repository;

        public GetInsumosQueryHandler(IInsumoRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public async Task<IReadOnlyList<InsumoCatalogoDto>> Handle(
            GetInsumosQuery query,
            CancellationToken cancellationToken = default)
        {
            var insumos = await _repository.GetFilteredAsync(
                query.Search,
                query.SoloActivos,
                cancellationToken);

            return insumos.Select(i => i.ToDto()).ToList();
        }
    }
}
