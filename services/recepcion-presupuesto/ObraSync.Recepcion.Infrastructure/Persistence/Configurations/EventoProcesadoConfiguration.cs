using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ObraSync.Recepcion.Infrastructure.Persistence.Configurations;

public class EventoProcesadoConfiguration : IEntityTypeConfiguration<EventoProcesado>
{
    public void Configure(EntityTypeBuilder<EventoProcesado> builder)
    {
        builder.ToTable("EventosProcesados");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .UseIdentityColumn();

        builder.Property(e => e.TipoEvento)
            .IsRequired();

        builder.Property(e => e.ProcesadoEn)
            .IsRequired();

        // Índice único: la BD misma impide procesar dos veces el mismo EventId
        builder.HasIndex(e => e.EventId)
            .IsUnique();
    }
}
