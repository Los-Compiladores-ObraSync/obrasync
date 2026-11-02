using Microsoft.EntityFrameworkCore;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Infrastructure;

public class LicitacionesDbContext : DbContext
{
    public LicitacionesDbContext(DbContextOptions<LicitacionesDbContext> options) : base(options) { }

    public DbSet<EventoProcesado> EventosProcesados => Set<EventoProcesado>();
    public DbSet<RequisicionAprobada> RequisicionesAprobadas => Set<RequisicionAprobada>();
    public DbSet<RequisicionAprobadaItem> RequisicionAprobadaItems => Set<RequisicionAprobadaItem>();
    public DbSet<Licitacion> Licitaciones => Set<Licitacion>();
    public DbSet<LicitacionItem> LicitacionItems => Set<LicitacionItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Índice único: la base de datos misma impide procesar dos veces el mismo EventId
        modelBuilder.Entity<EventoProcesado>()
            .HasIndex(e => e.EventId)
            .IsUnique();

        modelBuilder.Entity<RequisicionAprobada>(e =>
        {
            e.HasKey(x => x.RequisicionId);
            e.Property(x => x.NumeroRequisicion).HasMaxLength(50);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.RequisicionId);
        });

        modelBuilder.Entity<RequisicionAprobadaItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Cantidad).HasPrecision(18, 3);
            e.Property(x => x.Descripcion).HasMaxLength(300);
            e.Property(x => x.UnidadMedida).HasMaxLength(20);
        });

        modelBuilder.Entity<Licitacion>(e =>
        {
            e.HasKey(x => x.LicitacionId);
            e.HasIndex(x => x.RequisicionId);
            e.Property(x => x.NumeroRequisicion).HasMaxLength(50);
            e.Property(x => x.Estado).HasConversion<string>().HasMaxLength(20);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.LicitacionId);
        });

        modelBuilder.Entity<LicitacionItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Cantidad).HasPrecision(18, 3);
            e.Property(x => x.Descripcion).HasMaxLength(300);
            e.Property(x => x.UnidadMedida).HasMaxLength(20);
        });
    }
}