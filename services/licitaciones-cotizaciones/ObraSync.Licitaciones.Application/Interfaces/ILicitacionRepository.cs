using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.Interfaces;

public interface ILicitacionRepository
{
    Task<RequisicionAprobada?> ObtenerRequisicionAsync(
        Guid requisicionId,
        CancellationToken ct = default);

    Task<bool> ExisteAbiertaAsync(
        Guid requisicionId,
        CancellationToken ct = default);

    Task AgregarAsync(
        Licitacion licitacion,
        CancellationToken ct = default);

    Task<Licitacion?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Licitacion>> GetFilteredAsync(
        Guid? obraId = null,
        string? estado = null,
        string? rolUsuario = null,
        CancellationToken cancellationToken = default);
}
