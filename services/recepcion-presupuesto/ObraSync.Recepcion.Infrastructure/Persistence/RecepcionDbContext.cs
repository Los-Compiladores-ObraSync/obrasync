using MassTransit;
using Microsoft.EntityFrameworkCore;
using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Domain.Entities;
using System.Reflection;

namespace ObraSync.Recepcion.Infrastructure.Persistence;

public class RecepcionDbContext : DbContext, IRecepcionDbContext
{
    public RecepcionDbContext(DbContextOptions<RecepcionDbContext> options) : base(options)
    {
    }

    public DbSet<Domain.Entities.Recepcion> Recepciones => Set<Domain.Entities.Recepcion>();
    public DbSet<DetalleRecepcion> DetallesRecepcion => Set<DetalleRecepcion>();
    public DbSet<PartidaPresupuestaria> PartidasPresupuestarias => Set<PartidaPresupuestaria>();
    public DbSet<OrdenCompra> OrdenesCompra => Set<OrdenCompra>();
    public DbSet<OrdenCompraItem> OrdenesCompraItems => Set<OrdenCompraItem>();

    // Exclusivo de la clase concreta para idempotencia en consumidores de Infrastructure
    public DbSet<EventoProcesado> EventosProcesados => Set<EventoProcesado>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Tablas del Outbox transaccional de MassTransit (#171)
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();

        // Aplica automáticamente todas las configuraciones IEntityTypeConfiguration del ensamblado actual
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // En proveedores no relacionales (InMemory en tests), simulamos la atomicidad transaccional
        // y la actualización automática de la columna rowversion de SQL Server (#195).
        if (!Database.IsRelational())
        {
            foreach (var entry in ChangeTracker.Entries<PartidaPresupuestaria>())
            {
                if (entry.State == EntityState.Modified)
                {
                    var originalRowVersion = entry.Property(p => p.RowVersion).OriginalValue;
                    var actualEnStore = await PartidasPresupuestarias
                        .AsNoTracking()
                        .Where(p => p.Id == entry.Entity.Id)
                        .Select(p => p.RowVersion)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (actualEnStore is not null && !actualEnStore.SequenceEqual(originalRowVersion))
                    {
                        throw new DbUpdateConcurrencyException(
                            "Conflicto de concurrencia optimista en PartidaPresupuestaria.");
                    }

                    entry.Property(p => p.RowVersion).CurrentValue = Guid.NewGuid().ToByteArray();
                }
                else if (entry.State == EntityState.Added)
                {
                    entry.Property(p => p.RowVersion).CurrentValue = Guid.NewGuid().ToByteArray();
                }
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}
