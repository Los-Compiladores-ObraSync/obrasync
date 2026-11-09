using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ObraSync.Licitaciones.Domain.Entities;
using MassTransit;

namespace ObraSync.Licitaciones.Infrastructure;

public class LicitacionesDbContext : DbContext
{
    public LicitacionesDbContext(DbContextOptions<LicitacionesDbContext> options) : base(options) { }

    public DbSet<EventoProcesado> EventosProcesados => Set<EventoProcesado>();
    public DbSet<RequisicionAprobada> RequisicionesAprobadas => Set<RequisicionAprobada>();
    public DbSet<RequisicionAprobadaItem> RequisicionAprobadaItems => Set<RequisicionAprobadaItem>();
    public DbSet<Licitacion> Licitaciones => Set<Licitacion>();
    public DbSet<LicitacionItem> LicitacionItems => Set<LicitacionItem>();
    public DbSet<Cotizacion> Cotizaciones => Set<Cotizacion>();
    public DbSet<CotizacionItem> CotizacionItems => Set<CotizacionItem>();
    public DbSet<OrdenCompra> OrdenesCompra => Set<OrdenCompra>();
    public DbSet<OrdenCompraItem> OrdenCompraItems => Set<OrdenCompraItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var utcDateTimeConverter = new ValueConverter<DateTime, DateTime>(
            v => v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v, DateTimeKind.Utc),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        // Índice único: la base de datos misma impide procesar dos veces el mismo EventId
        modelBuilder.Entity<EventoProcesado>()
            .HasIndex(e => e.EventId)
            .IsUnique();

        modelBuilder.Entity<RequisicionAprobada>(e =>
        {
            e.HasKey(x => x.RequisicionId);
            e.Property(x => x.NumeroRequisicion).HasMaxLength(50);
            e.Property(x => x.Etapa).HasMaxLength(200);
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

        modelBuilder.Entity<Cotizacion>(e =>
        {
            e.ToTable("Cotizaciones");
            e.HasKey(x => x.CotizacionId);
            e.HasIndex(x => x.LicitacionId);
            e.HasIndex(x => new { x.LicitacionId, x.ProveedorId }).IsUnique();
            e.Property(x => x.Flete).HasPrecision(18, 2);
            e.Property(x => x.FechaCreacion).HasConversion(utcDateTimeConverter);

            e.HasOne<Licitacion>()
                .WithMany()
                .HasForeignKey(x => x.LicitacionId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(x => x.Items)
                .WithOne()
                .HasForeignKey(i => i.CotizacionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CotizacionItem>(e =>
        {
            e.ToTable("CotizacionItems");
            e.HasKey(x => x.Id);
            e.Property(x => x.PrecioUnitario).HasPrecision(18, 2);
        });

        modelBuilder.Entity<OrdenCompra>(e =>
        {
            e.ToTable("OrdenesCompra");
            e.HasKey(x => x.OrdenCompraId);
            e.HasIndex(x => x.LicitacionId);
            e.Property(x => x.Estado).HasMaxLength(20);
            e.Property(x => x.TotalFlete).HasPrecision(18, 2);
            e.Property(x => x.FechaEmision).HasConversion(utcDateTimeConverter);

            e.HasMany(x => x.Items)
                .WithOne()
                .HasForeignKey(i => i.OrdenCompraId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrdenCompraItem>(e =>
        {
            e.ToTable("OrdenCompraItems");
            e.HasKey(x => x.Id);
            e.Property(x => x.Cantidad).HasPrecision(18, 3);
            e.Property(x => x.PrecioUnitario).HasPrecision(18, 2);
            e.Property(x => x.Descripcion).HasMaxLength(300);
            e.Property(x => x.UnidadMedida).HasMaxLength(20);
        });

        // Tablas del outbox de MassTransit (#135)
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
