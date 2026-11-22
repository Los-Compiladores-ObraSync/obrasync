using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.Interfaces;

public interface IChatOrdenRepository
{
    Task<PaginaDe<MensajeChatOrden>> GetMensajesPorOrdenAsync(
        Guid ordenCompraId,
        ParametrosPagina paginacion,
        CancellationToken cancellationToken = default);

    /// <summary>Agrega el mensaje al DbContext para guardarse dentro de la transacción de UnitOfWork.</summary>
    Task AddMensajeAsync(MensajeChatOrden mensaje, CancellationToken cancellationToken = default);

    Task<LecturaChatOrden> UpsertLecturaAsync(
        Guid ordenCompraId,
        Guid usuarioId,
        DateTime ultimaLecturaUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, int>> GetConteoNoLeidosPorOrdenIdsAsync(
        IReadOnlyCollection<Guid> ordenCompraIds,
        Guid usuarioId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NoLeidosOrdenDto>> GetNoLeidosPorUsuarioAsync(
        Guid usuarioId,
        bool soloProveedor,
        CancellationToken cancellationToken = default);
}
