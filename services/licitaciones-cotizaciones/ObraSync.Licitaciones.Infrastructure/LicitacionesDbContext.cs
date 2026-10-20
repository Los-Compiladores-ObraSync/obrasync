using Microsoft.EntityFrameworkCore;

namespace ObraSync.Licitaciones.Infrastructure;

public class LicitacionesDbContext : DbContext
{
    public LicitacionesDbContext(DbContextOptions<LicitacionesDbContext> options) : base(options) { }

    public DbSet<EventoProcesado> EventosProcesados => Set<EventoProcesado>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Índice único: la base de datos misma impide procesar dos veces el mismo EventId
        modelBuilder.Entity<EventoProcesado>()
            .HasIndex(e => e.EventId)
            .IsUnique();
    }
}