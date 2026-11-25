namespace ObraSync.Licitaciones.Application.Interfaces;

public interface ILicitacionEventPublisher
{
    Task PublishAsync<TEvent>(TEvent evento, CancellationToken cancellationToken = default)
        where TEvent : class;
}
