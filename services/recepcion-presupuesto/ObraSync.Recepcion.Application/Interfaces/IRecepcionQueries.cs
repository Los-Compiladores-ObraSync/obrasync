using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Queries;

namespace ObraSync.Recepcion.Application.Interfaces;

/// <summary>
/// Puerto de consultas de solo lectura para recepciones y órdenes de compra asociadas (CQRS / ISP / DIP).
/// Evita filtrar <c>IQueryable</c> o <c>DbContext</c> de EF Core en la capa de Aplicación.
/// </summary>
public interface IRecepcionQueries
{
    Task<PaginaDe<RecepcionDto>> ObtenerRecepcionesAsync(ObtenerRecepcionesQuery query, CancellationToken cancellationToken = default);
    Task<RecepcionDto?> ObtenerRecepcionPorIdAsync(Guid recepcionId, CancellationToken cancellationToken = default);
    Task<RecepcionesDeOrdenDto?> ObtenerRecepcionesPorOrdenAsync(Guid ordenCompraId, CancellationToken cancellationToken = default);
    Task<FotoDescargaDto?> ObtenerFotoRecepcionAsync(Guid recepcionId, Guid fotoId, CancellationToken cancellationToken = default);
    Task<PaginaDe<OrdenPendienteDto>> ObtenerOrdenesPendientesAsync(ObtenerOrdenesPendientesQuery query, CancellationToken cancellationToken = default);
    Task<PaginaDe<OrdenBloqueadaDto>> ObtenerOrdenesBloqueadasAsync(ObtenerOrdenesBloqueadasQuery query, CancellationToken cancellationToken = default);
}
