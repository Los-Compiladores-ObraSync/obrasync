using System.Threading;
using System.Threading.Tasks;
using ObraSync.Contracts.Events;

namespace ObraSync.Requisiciones.Application.Interfaces
{
    public interface IRequisicionEventPublisher
    {
        Task PublishRequisicionAprobadaAsync(
            RequisicionAprobadaEvent evento,
            CancellationToken cancellationToken = default);
    }
}
