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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Aplica automáticamente todas las configuraciones IEntityTypeConfiguration del ensamblado actual
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
