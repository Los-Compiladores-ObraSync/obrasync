using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ObraSync.Requisiciones.Domain.Constants;
using ObraSync.Requisiciones.Domain.Entities;

namespace ObraSync.Requisiciones.Infrastructure.Persistence.Configurations
{
    public class InsumoConfiguration : IEntityTypeConfiguration<Insumo>
    {
        public void Configure(EntityTypeBuilder<Insumo> builder)
        {
            builder.ToTable("Insumos");

            builder.HasKey(i => i.Id);

            builder.Property(i => i.Codigo)
                .HasMaxLength(50)
                .IsRequired();

            builder.HasIndex(i => i.Codigo)
                .IsUnique();

            builder.Property(i => i.Descripcion)
                .HasMaxLength(250)
                .IsRequired();

            builder.Property(i => i.UnidadMedida)
                .HasMaxLength(20)
                .IsRequired();

            builder.Property(i => i.Moneda)
                .HasMaxLength(3)
                .IsRequired()
                .HasDefaultValue(Monedas.PesoArgentino);

            builder.Property(i => i.PrecioReferencia)
                .HasPrecision(18, 2);

            builder.Property(i => i.Activo)
                .IsRequired()
                .HasDefaultValue(true);

            // Datos iniciales de catálogo para Obra Gruesa (MVP - Divisa única ARS)
            builder.HasData(
                new Insumo
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111110001"),
                    Codigo = "MAT-CEM-001",
                    Descripcion = "Cemento Portland Normal (bolsa 50 kg)",
                    UnidadMedida = UnidadesMedida.Bolsa,
                    Moneda = Monedas.PesoArgentino,
                    PrecioReferencia = 8500.00m,
                    Activo = true
                },
                new Insumo
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111110002"),
                    Codigo = "MAT-CEM-002",
                    Descripcion = "Cemento de Albañilería Plasticor (bolsa 40 kg)",
                    UnidadMedida = UnidadesMedida.Bolsa,
                    Moneda = Monedas.PesoArgentino,
                    PrecioReferencia = 7200.00m,
                    Activo = true
                },
                new Insumo
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111110003"),
                    Codigo = "MAT-CAL-001",
                    Descripcion = "Cal Hidratada en Polvo (bolsa 25 kg)",
                    UnidadMedida = UnidadesMedida.Bolsa,
                    Moneda = Monedas.PesoArgentino,
                    PrecioReferencia = 4800.00m,
                    Activo = true
                },
                new Insumo
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111110004"),
                    Codigo = "MAT-ARI-001",
                    Descripcion = "Arena Gruesa Lavada de Río",
                    UnidadMedida = UnidadesMedida.MetroCubico,
                    Moneda = Monedas.PesoArgentino,
                    PrecioReferencia = 28000.00m,
                    Activo = true
                },
                new Insumo
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111110005"),
                    Codigo = "MAT-ARI-002",
                    Descripcion = "Piedra Partida Granítica 6-20 mm",
                    UnidadMedida = UnidadesMedida.MetroCubico,
                    Moneda = Monedas.PesoArgentino,
                    PrecioReferencia = 42000.00m,
                    Activo = true
                },
                new Insumo
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111110006"),
                    Codigo = "MAT-ARI-003",
                    Descripcion = "Cascote Picado Limpio de Ladrillo",
                    UnidadMedida = UnidadesMedida.MetroCubico,
                    Moneda = Monedas.PesoArgentino,
                    PrecioReferencia = 19500.00m,
                    Activo = true
                },
                new Insumo
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111110007"),
                    Codigo = "MAT-HIE-001",
                    Descripcion = "Hierro Conformado ADN 420 Ø 8 mm (barra 12 m)",
                    UnidadMedida = UnidadesMedida.Unidad,
                    Moneda = Monedas.PesoArgentino,
                    PrecioReferencia = 9600.00m,
                    Activo = true
                },
                new Insumo
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111110008"),
                    Codigo = "MAT-HIE-002",
                    Descripcion = "Hierro Conformado ADN 420 Ø 10 mm (barra 12 m)",
                    UnidadMedida = UnidadesMedida.Unidad,
                    Moneda = Monedas.PesoArgentino,
                    PrecioReferencia = 14800.00m,
                    Activo = true
                },
                new Insumo
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111110009"),
                    Codigo = "MAT-HIE-003",
                    Descripcion = "Hierro Conformado ADN 420 Ø 12 mm (barra 12 m)",
                    UnidadMedida = UnidadesMedida.Unidad,
                    Moneda = Monedas.PesoArgentino,
                    PrecioReferencia = 21200.00m,
                    Activo = true
                },
                new Insumo
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111110010"),
                    Codigo = "MAT-MAL-001",
                    Descripcion = "Malla Electrosoldada Standard Q188 15x15 cm 6 mm (panel 5x2.4 m)",
                    UnidadMedida = UnidadesMedida.Unidad,
                    Moneda = Monedas.PesoArgentino,
                    PrecioReferencia = 68500.00m,
                    Activo = true
                },
                new Insumo
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111110011"),
                    Codigo = "MAT-ALA-001",
                    Descripcion = "Alambre Negro Recocido de Atar Nº 16",
                    UnidadMedida = UnidadesMedida.Kilogramo,
                    Moneda = Monedas.PesoArgentino,
                    PrecioReferencia = 3400.00m,
                    Activo = true
                }
            );
        }
    }
}
