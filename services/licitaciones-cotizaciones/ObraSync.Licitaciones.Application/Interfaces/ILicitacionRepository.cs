using ObraSync.Licitaciones.Application.DTOs;
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

    /// <summary>La licitación más nueva de una requisición (puede haber más de una si se cerró y se volvió a publicar).</summary>
    Task<Licitacion?> GetMasRecientePorRequisicionAsync(
        Guid requisicionId,
        CancellationToken ct = default);

    Task<Licitacion?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        Licitacion licitacion,
        CancellationToken ct = default);

    Task<PaginaDe<Licitacion>> GetFilteredAsync(
        Guid? obraId = null,
        string? estado = null,
        string? rolUsuario = null,
        ParametrosPagina? paginacion = null,
        CancellationToken ct = default);
}
