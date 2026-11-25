using ObraSync.Recepcion.Application.Interfaces;

namespace ObraSync.Recepcion.Application.Common.Interfaces;

/// <summary>
/// Contexto agregado de persistencia de Aplicación sin acoplamiento a EF Core (<c>DbSet</c>/<c>DbContext</c>).
/// Agrupa repositorios y unidad de trabajo para tests o consumidores que reciben un único contexto.
/// </summary>
public interface IRecepcionPersistenceContext :
    IOrdenCompraRepository,
    IRecepcionRepository,
    IPartidaPresupuestariaRepository,
    IUnitOfWork
{
}
