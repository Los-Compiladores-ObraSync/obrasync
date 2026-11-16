using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ObraSync.Recepcion.Domain.Entities;

namespace ObraSync.Recepcion.Infrastructure.Persistence.Configurations;

public class PartidaPresupuestariaConfiguration : IEntityTypeConfiguration<PartidaPresupuestaria>
{
    public void Configure(EntityTypeBuilder<PartidaPresupuestaria> builder)
    {
        builder.ToTable("PartidasPresupuestarias");

        builder.HasKey(p => p.Id);

        // ObraId es un GUID que apunta al servicio Usuarios y Obras.
        builder.HasIndex(p => p.ObraId);

        // Claves únicas de negocio (#195):
        // 1. No pueden existir dos partidas con el mismo código en la misma obra.
        builder.HasIndex(p => new { p.ObraId, p.CodigoPartida })
            .IsUnique();

        // 2. Solo puede existir una partida por (ObraId, Etapa), incluyendo la partida
        //    general de toda la obra (Etapa = NULL). HasFilter(null) evita el filtro
        //    por defecto "[Etapa] IS NOT NULL" de EF Core en SQL Server.
        builder.HasIndex(p => new { p.ObraId, p.Etapa })
            .IsUnique()
            .HasFilter(null);

        builder.Property(p => p.RowVersion)
            .IsRowVersion();

        builder.Property(p => p.Etapa)
            .HasMaxLength(200);

        builder.Property(p => p.CodigoPartida)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(p => p.Descripcion)
            .IsRequired()
            .HasMaxLength(250);

        builder.Property(p => p.Moneda)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(p => p.MontoAsignado)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(p => p.MontoComprometido)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(p => p.MontoEjecutado)
            .HasPrecision(18, 2)
            .IsRequired();
    }
}
