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

        // Aplica automáticamente todas las configuraciones IEntityTypeConfiguration del ensamblado actual
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
