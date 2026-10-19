using System;
using System.Collections.Generic;
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

        public async Task<IReadOnlyList<RequisicionResponseDto>> Handle(
            GetRequisicionesQuery query,
            CancellationToken cancellationToken = default)
        {
            var requisiciones = await _repository.GetFilteredAsync(
                query.ObraId,
                query.Estado,
                cancellationToken);

            return requisiciones.Select(r => r.ToDto()).ToList();
        }
    }
}
