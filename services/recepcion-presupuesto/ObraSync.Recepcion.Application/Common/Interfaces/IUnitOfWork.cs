namespace ObraSync.Recepcion.Application.Common.Interfaces;

/// <summary>
/// Abstracción segregada de unidad de trabajo (ISP / DIP) para persistir cambios transaccionales
/// (incluyendo mensajes encolados en el Outbox) sin acoplar al consumidor a los DbSet de EF Core.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
