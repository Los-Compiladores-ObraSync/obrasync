namespace ObraSync.Recepcion.Application.Common.Interfaces;

/// <summary>
/// Abstracción de caso de uso de consulta (DIP) para desacoplar la capa de presentación de los handlers concretos.
/// </summary>
public interface IQueryHandler<in TQuery, TResult>
{
    Task<TResult> Handle(TQuery query, CancellationToken cancellationToken = default);
}
