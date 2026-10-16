using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ObraSync.Recepcion.Domain.Entities;

namespace ObraSync.Recepcion.Infrastructure.Persistence.Configurations;

public class DetalleRecepcionConfiguration : IEntityTypeConfiguration<DetalleRecepcion>
{
    public void Configure(EntityTypeBuilder<DetalleRecepcion> builder)
    {
        builder.ToTable("DetallesRecepcion");

        builder.HasKey(d => d.Id);

        // ArticuloId es un GUID que apunta al servicio de Requisiciones y Catálogo.
        // No hay FK en la base de datos de SQL Server.
        builder.HasIndex(d => d.ArticuloId);

        builder.Property(d => d.DescripcionArticulo)
            .IsRequired()
            .HasMaxLength(250);

        builder.Property(d => d.UnidadMedida)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(d => d.CantidadEsperada)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(d => d.CantidadRecibida)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(d => d.Observacion)
            .HasMaxLength(500);
    }
}
