using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ObraSync.Requisiciones.Domain.Entities;

namespace ObraSync.Requisiciones.Infrastructure.Persistence.Configurations
{
    public class HistorialRechazoRequisicionConfiguration : IEntityTypeConfiguration<HistorialRechazoRequisicion>
    {
        public void Configure(EntityTypeBuilder<HistorialRechazoRequisicion> builder)
        {
            var utcDateTimeConverter = new ValueConverter<DateTime, DateTime>(
                value => value.ToUniversalTime(),
                value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

            builder.ToTable("HistorialRechazosRequisicion");

            builder.HasKey(h => h.Id);

            builder.Property(h => h.Id)
                .ValueGeneratedNever();

            builder.Property(h => h.RequisicionId)
                .IsRequired();

            builder.HasIndex(h => h.RequisicionId);

            builder.Property(h => h.RechazadorId);

            builder.Property(h => h.Motivo)
                .HasMaxLength(500)
                .IsRequired();

            builder.Property(h => h.FechaRechazo)
                .HasConversion(utcDateTimeConverter)
                .IsRequired();
        }
    }
}
