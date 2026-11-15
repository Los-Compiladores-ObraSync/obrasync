using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObraSync.Requisiciones.Application.DTOs;
using ObraSync.Requisiciones.Application.Interfaces;
using ObraSync.Requisiciones.Application.Mappings;
using ObraSync.Requisiciones.Application.UseCases.Requisiciones.Queries;

namespace ObraSync.Requisiciones.Application.UseCases.Requisiciones.Handlers
{
    public class GetRequisicionesQueryHandler
    {
        private readonly IRequisicionRepository _repository;

        public GetRequisicionesQueryHandler(IRequisicionRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public async Task<PaginaDe<RequisicionResponseDto>> Handle(
            GetRequisicionesQuery query,
            CancellationToken cancellationToken = default)
        {
            var pagina = await _repository.GetFilteredAsync(
                query.ObraId,
                query.Estado,
                query.SolicitanteId,
                query.Paginacion,
                cancellationToken);

            var items = pagina.Items.Select(r => r.ToDto()).ToList();
            return new PaginaDe<RequisicionResponseDto>(items, pagina.Total);
        }
    }
}
