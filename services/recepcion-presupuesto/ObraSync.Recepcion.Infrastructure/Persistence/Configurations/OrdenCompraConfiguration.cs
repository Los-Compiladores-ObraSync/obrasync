using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Domain.Enums;

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

        builder.Property(o => o.Estado)
            .IsRequired()
            .HasConversion<int>()
            .HasDefaultValue(EstadoOrdenCompra.Activa);

        builder.Property(o => o.MotivoBloqueo)
            .HasMaxLength(500);

        builder.Property(o => o.FechaBloqueo);

        builder.Property(o => o.LiberadaPorUsuarioId);

        builder.Property(o => o.LiberadaEn);

        builder.Property(o => o.MotivoLiberacion)
            .HasMaxLength(500);

        builder.Property(o => o.SinPartidaPresupuestaria)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(o => o.FechaDespacho);

        // Índices para optimizar búsquedas y listados paginados por obra/estado/fecha (#204, #210)
        builder.HasIndex(o => o.ObraId);
        builder.HasIndex(o => o.Estado);
        builder.HasIndex(o => o.FechaBloqueo);
        builder.HasIndex(o => o.SinPartidaPresupuestaria);
        builder.HasIndex(o => o.FechaDespacho);

        // Relación 1 a N con Items en cascada
        builder.HasMany(o => o.Items)
            .WithOne(i => i.OrdenCompra)
            .HasForeignKey(i => i.OrdenCompraId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
