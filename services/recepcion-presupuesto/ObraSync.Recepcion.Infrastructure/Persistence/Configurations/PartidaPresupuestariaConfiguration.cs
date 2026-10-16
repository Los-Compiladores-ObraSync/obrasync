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
