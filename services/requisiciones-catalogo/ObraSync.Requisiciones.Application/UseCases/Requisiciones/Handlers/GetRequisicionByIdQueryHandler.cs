using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ObraSync.Requisiciones.Application.DTOs;
using ObraSync.Requisiciones.Application.Interfaces;
using ObraSync.Requisiciones.Application.Mappings;
using ObraSync.Requisiciones.Application.UseCases.Requisiciones.Queries;

namespace ObraSync.Requisiciones.Application.UseCases.Requisiciones.Handlers
{
    public class GetRequisicionByIdQueryHandler
    {
        private readonly IRequisicionRepository _repository;

        public GetRequisicionByIdQueryHandler(IRequisicionRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public async Task<RequisicionResponseDto> Handle(
            GetRequisicionByIdQuery query,
            CancellationToken cancellationToken = default)
        {
            var requisicion = await _repository.GetByIdAsync(query.RequisicionId, cancellationToken);
            if (requisicion == null)
            {
                throw new KeyNotFoundException($"No se encontró la requisición con Id '{query.RequisicionId}'.");
            }

            return requisicion.ToDto();
        }
    }
}
