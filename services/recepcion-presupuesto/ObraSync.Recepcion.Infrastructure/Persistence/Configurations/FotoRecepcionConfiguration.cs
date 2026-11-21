using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ObraSync.Recepcion.Domain.Entities;

namespace ObraSync.Recepcion.Infrastructure.Persistence.Configurations;

public class FotoRecepcionConfiguration : IEntityTypeConfiguration<FotoRecepcion>
{
    public void Configure(EntityTypeBuilder<FotoRecepcion> builder)
    {
        builder.ToTable("FotosRecepcion");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.RecepcionId)
            .IsRequired();

        builder.Property(f => f.NombreArchivo)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(f => f.ContentType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(f => f.TamanoBytes)
            .IsRequired();

        builder.Property(f => f.Contenido)
            .IsRequired()
            .HasColumnType("varbinary(max)");

        builder.Property(f => f.Orden)
            .IsRequired();

        builder.HasIndex(f => f.RecepcionId);

        builder.HasIndex(f => new { f.RecepcionId, f.Orden })
            .IsUnique();
    }
}
