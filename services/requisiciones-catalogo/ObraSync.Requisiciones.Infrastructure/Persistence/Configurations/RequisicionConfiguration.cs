using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ObraSync.Requisiciones.Domain.Constants;
using ObraSync.Requisiciones.Domain.Entities;

namespace ObraSync.Requisiciones.Infrastructure.Persistence.Configurations
{
    public class RequisicionConfiguration : IEntityTypeConfiguration<Requisicion>
    {
        public void Configure(EntityTypeBuilder<Requisicion> builder)
        {
            builder.ToTable("Requisiciones");

            builder.HasKey(r => r.Id);

            builder.Property(r => r.NumeroRequisicion)
                .HasMaxLength(30)
                .IsRequired();

            builder.HasIndex(r => r.NumeroRequisicion)
                .IsUnique();

            builder.Property(r => r.ObraId)
                .IsRequired();

            builder.HasIndex(r => r.ObraId);

            builder.Property(r => r.Etapa)
                .HasMaxLength(100);

            builder.Property(r => r.SolicitanteId)
                .IsRequired();

            builder.Property(r => r.AprobadorId);

            builder.Property(r => r.Estado)
                .HasMaxLength(30)
                .IsRequired()
                .HasDefaultValue(EstadoRequisicion.Pendiente);

            builder.Property(r => r.Observaciones)
                .HasMaxLength(500);

            builder.Property(r => r.FechaCreacion)
                .IsRequired();

            builder.Property(r => r.FechaAprobacion);

            builder.HasMany(r => r.Items)
                .WithOne()
                .HasForeignKey(i => i.RequisicionId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
