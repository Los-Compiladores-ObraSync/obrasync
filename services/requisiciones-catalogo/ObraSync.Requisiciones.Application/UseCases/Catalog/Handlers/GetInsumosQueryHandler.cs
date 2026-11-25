using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObraSync.Requisiciones.Application.DTOs;
using ObraSync.Requisiciones.Application.Interfaces;
using ObraSync.Requisiciones.Application.Mappings;
using ObraSync.Requisiciones.Application.UseCases.Catalog.Queries;

namespace ObraSync.Requisiciones.Application.UseCases.Catalog.Handlers
{
    public class GetInsumosQueryHandler : IQueryHandler<GetInsumosQuery, PaginaDe<InsumoCatalogoDto>>
    {
        private readonly IInsumoRepository _repository;

        public GetInsumosQueryHandler(IInsumoRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public async Task<PaginaDe<InsumoCatalogoDto>> Handle(
            GetInsumosQuery query,
            CancellationToken cancellationToken = default)
        {
            var pagina = await _repository.GetFilteredAsync(
                query.Search,
                query.SoloActivos,
                query.Paginacion,
                cancellationToken);

            var items = pagina.Items.Select(i => i.ToDto()).ToList();
            return new PaginaDe<InsumoCatalogoDto>(items, pagina.Total);
        }
    }
}
