using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ObraSync.Recepcion.Domain.Entities;

namespace ObraSync.Recepcion.Infrastructure.Persistence.Configurations;

public class OrdenCompraItemConfiguration : IEntityTypeConfiguration<OrdenCompraItem>
{
    public void Configure(EntityTypeBuilder<OrdenCompraItem> builder)
    {
        builder.ToTable("OrdenesCompraItems");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Cantidad)
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(i => i.PrecioUnitario)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(i => i.UnidadMedida)
            .IsRequired()
            .HasMaxLength(50);

        // Descripcion sin límite explícito (decisión pendiente, queda nvarchar(max))
        builder.Property(i => i.Descripcion);

        // Índice NO único sobre (OrdenCompraId, ArticuloId)
        builder.HasIndex(i => new { i.OrdenCompraId, i.ArticuloId });
    }
}
