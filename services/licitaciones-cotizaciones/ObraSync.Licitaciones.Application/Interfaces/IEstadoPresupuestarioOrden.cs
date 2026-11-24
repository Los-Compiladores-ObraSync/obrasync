namespace ObraSync.Licitaciones.Application.Interfaces;

public interface IEstadoPresupuestarioOrden
{
    Task<bool?> EstaBloqueadaAsync(
        Guid ordenCompraId,
        CancellationToken cancellationToken);
}