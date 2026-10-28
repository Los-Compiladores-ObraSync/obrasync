using Microsoft.EntityFrameworkCore;
using ObraSync.Recepcion.Domain.Entities;

namespace ObraSync.Recepcion.Application.Common.Interfaces;

public interface IRecepcionDbContext
{
    DbSet<Domain.Entities.Recepcion> Recepciones { get; }
    DbSet<DetalleRecepcion> DetallesRecepcion { get; }
    DbSet<PartidaPresupuestaria> PartidasPresupuestarias { get; }
    DbSet<OrdenCompra> OrdenesCompra { get; }
    DbSet<OrdenCompraItem> OrdenesCompraItems { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
