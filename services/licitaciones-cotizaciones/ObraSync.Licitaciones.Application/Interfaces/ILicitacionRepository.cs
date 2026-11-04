using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.Interfaces;

public interface ILicitacionRepository
{
    Task<RequisicionAprobada?> ObtenerRequisicionAsync(Guid requisicionId, CancellationToken ct);
    Task<bool> ExisteAbiertaAsync(Guid requisicionId, CancellationToken ct);
    Task AgregarAsync(Licitacion licitacion, CancellationToken ct);
}