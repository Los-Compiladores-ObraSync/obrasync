using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ObraSync.Recepcion.Domain.Entities;

namespace ObraSync.Recepcion.Infrastructure.Persistence.Configurations;

public class RecepcionConfiguration : IEntityTypeConfiguration<Domain.Entities.Recepcion>
{
    public void Configure(EntityTypeBuilder<Domain.Entities.Recepcion> builder)
    {
        builder.ToTable("Recepciones");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.NumeroRemito)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(r => r.Observaciones)
            .HasMaxLength(500);

        builder.Property(r => r.Estado)
            .IsRequired()
            .HasConversion<int>();

        // Índices para optimizar búsquedas por Obra y por Orden de Compra
        // NOTA: ObraId y OrdenCompraId son GUIDs de otros microservicios (Database-per-Service).
        // NO se configuran Foreign Keys hacia otras bases de datos.
        builder.HasIndex(r => r.ObraId);
        builder.HasIndex(r => r.OrdenCompraId);
        builder.HasIndex(r => r.RecepcionadoPorUsuarioId);

        // Relación 1 a N INTERNA dentro del mismo microservicio y base (DB_Recepcion)
        builder.HasMany(r => r.Detalles)
            .WithOne(d => d.Recepcion)
            .HasForeignKey(d => d.RecepcionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
