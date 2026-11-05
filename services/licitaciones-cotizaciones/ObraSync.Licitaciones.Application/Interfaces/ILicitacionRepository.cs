using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.Interfaces;

public interface ILicitacionRepository
{
    Task<RequisicionAprobada?> ObtenerRequisicionAsync(
        Guid requisicionid,
        CancellationToken ct = default);

    Task<bool> ExisteAbiertaAsync(
        Guid requisicionid,
        CancellationToken ct = default);

    Task AgregarAsync(
        Licitacion licitacion,
        CancellationToken ct = default);

    Task<Licitacion?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        Licitacion licitacion,
        CancellationToken ct = default);

    Task<IReadOnlyList<Licitacion>> GetFilteredAsync(
        Guid? obraId = null,
        string? estado = null,
        string? rolUsuario = null,
        CancellationToken ct = default);
}