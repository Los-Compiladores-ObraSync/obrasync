using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ObraSync.Usuarios.Domain.Entities;

namespace ObraSync.Usuarios.Infrastructure.Data;

/// <summary>
/// DbContext de autenticación utilizando IdentityDbContext configurado con Guid como PK.
/// </summary>
public class AuthDbContext : IdentityDbContext<AppUser, AppRole, Guid>
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options)
    {
    }

    // DBSETS EXISTENTES Y DE PLANTILLAS
    public DbSet<Obra> Obras { get; set; }
    public DbSet<EtapaObra> EtapasObras { get; set; }
    public DbSet<PlantillaObra> PlantillasObra { get; set; }
    public DbSet<EtapaPlantilla> EtapasPlantilla { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<AppUser>(entity =>
        {
            // Renombro AspNetUsers a Usuarios
            entity.ToTable("Usuarios");

            entity.Property(u => u.NombreCompleto)
                .IsRequired()
                .HasMaxLength(150);

            entity.Property(u => u.FechaCreacion)
                .HasDefaultValueSql("GETUTCDATE()");
        });

        // CONFIGURACIÓN DE LA ENTIDAD OBRA
        builder.Entity<Obra>(entity =>
        {
            entity.ToTable("Obras");
            entity.HasKey(o => o.Id);

            entity.Property(o => o.Nombre)
                .IsRequired()
                .HasMaxLength(150);

            entity.Property(o => o.Ubicacion)
                .HasMaxLength(250);

            entity.Property(o => o.Activa)
                .IsRequired()
                .HasDefaultValue(true);

            // Índice único para evitar obras duplicadas (#110)
            entity.HasIndex(o => new { o.Nombre, o.Ubicacion })
                .IsUnique()
                .HasFilter("[Activa] = 1");

            // Relación con el Capataz (AppUser)
            entity.HasOne(o => o.Capataz)
                .WithMany()
                .HasForeignKey(o => o.CapatazId)
                .OnDelete(DeleteBehavior.Restrict);

            // Relación con las Etapas (1 Obra -> Muchas Etapas)
            entity.HasMany(o => o.Etapas)
                .WithOne(e => e.Obra)
                .HasForeignKey(e => e.ObraId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // CONFIGURACIÓN DE LA ENTIDAD ETAPA OBRA
        builder.Entity<EtapaObra>(entity =>
        {
            entity.ToTable("EtapasObras");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.NombreEtapa)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.Estado)
                .HasMaxLength(50)
                .HasDefaultValue("Pendiente");
        });

        // CONFIGURACIÓN DE LA ENTIDAD PLANTILLA OBRA
        builder.Entity<PlantillaObra>(entity =>
        {
            entity.ToTable("PlantillasObra");
            entity.HasKey(p => p.Id);

            entity.Property(p => p.Nombre)
                .IsRequired()
                .HasMaxLength(150);

            entity.Property(p => p.Descripcion)
                .HasMaxLength(500);

            // Relación con las Etapas de la Plantilla (1 Plantilla -> Muchas Etapas)
            entity.HasMany(p => p.Etapas)
                .WithOne(e => e.PlantillaObra)
                .HasForeignKey(e => e.PlantillaObraId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // CONFIGURACIÓN DE LA ENTIDAD ETAPA PLANTILLA
        builder.Entity<EtapaPlantilla>(entity =>
        {
            entity.ToTable("EtapasPlantilla");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.NombreEtapa)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.Orden)
                .IsRequired();
        });
    }
}
