using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ObraSync.Requisiciones.Domain.Entities;

namespace ObraSync.Requisiciones.Infrastructure.Persistence.Configurations
{
    public class RequisicionItemConfiguration : IEntityTypeConfiguration<RequisicionItem>
    {
        public void Configure(EntityTypeBuilder<RequisicionItem> builder)
        {
            builder.ToTable("RequisicionItems");

            builder.HasKey(i => i.Id);

            builder.Property(i => i.RequisicionId)
                .IsRequired();

            builder.Property(i => i.ArticuloId)
                .IsRequired();

            builder.Property(i => i.Descripcion)
                .HasMaxLength(250)
                .IsRequired();

            builder.Property(i => i.Cantidad)
                .HasPrecision(18, 2)
                .IsRequired();

            builder.Property(i => i.UnidadMedida)
                .HasMaxLength(20)
                .IsRequired();
        }
    }
}
