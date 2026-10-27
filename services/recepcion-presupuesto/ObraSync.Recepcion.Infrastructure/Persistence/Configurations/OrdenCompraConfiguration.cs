using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ObraSync.Recepcion.Domain.Entities;

namespace ObraSync.Recepcion.Infrastructure.Persistence.Configurations;

public class OrdenCompraConfiguration : IEntityTypeConfiguration<OrdenCompra>
{
    public void Configure(EntityTypeBuilder<OrdenCompra> builder)
    {
        builder.ToTable("OrdenesCompra");

        builder.HasKey(o => o.Id);

        // El Id es asignado externamente desde el evento (ordenCompraId), nunca generado por BD
        builder.Property(o => o.Id)
            .ValueGeneratedNever();

        builder.Property(o => o.MontoTotal)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(o => o.Etapa)
            .HasMaxLength(200);

        // Índice no único para optimizar búsquedas por Obra
        builder.HasIndex(o => o.ObraId);

        // Relación 1 a N con Items en cascada
        builder.HasMany(o => o.Items)
            .WithOne(i => i.OrdenCompra)
            .HasForeignKey(i => i.OrdenCompraId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
