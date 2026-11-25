namespace ObraSync.Recepcion.Application.Common.Interfaces;

/// <summary>
/// Abstracción de caso de uso de escritura (DIP) para desacoplar la capa de presentación de los handlers concretos.
/// </summary>
public interface ICommandHandler<in TCommand, TResult>
{
    Task<TResult> Handle(TCommand command, CancellationToken cancellationToken = default);
}
