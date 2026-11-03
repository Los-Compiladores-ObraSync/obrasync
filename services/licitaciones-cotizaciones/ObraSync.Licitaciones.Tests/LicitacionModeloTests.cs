using Microsoft.EntityFrameworkCore;
using ObraSync.Licitaciones.Domain.Entities;
using ObraSync.Licitaciones.Infrastructure;
using Xunit;

namespace ObraSync.Licitaciones.Tests;

public class LicitacionModeloTests
{
    private static LicitacionesDbContext CrearDb(string nombreBase)
    {
        var options = new DbContextOptionsBuilder<LicitacionesDbContext>()
            .UseInMemoryDatabase(nombreBase)
            .Options;
        return new LicitacionesDbContext(options);
    }

    [Fact]
    public async Task Licitacion_con_items_se_guarda_y_se_lee_de_vuelta()
    {
        var nombreBase = Guid.NewGuid().ToString();
        var licitacionId = Guid.NewGuid();
        var requisicionId = Guid.NewGuid();
        var obraId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        var fechaLimite = DateTime.UtcNow.AddDays(3);

        // Se guarda con un contexto...
        await using (var db = CrearDb(nombreBase))
        {
            db.Licitaciones.Add(new Licitacion
            {
                LicitacionId = licitacionId,
                RequisicionId = requisicionId,
                NumeroRequisicion = "REQ-001",
                ObraId = obraId,
                Estado = EstadoLicitacion.Abierta,
                FechaCreacion = DateTime.UtcNow,
                FechaLimiteCotizacion = fechaLimite,
                Items = new List<LicitacionItem>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        ArticuloId = articuloId,
                        Descripcion = "Cemento",
                        Cantidad = 80m,
                        UnidadMedida = "bolsa"
                    },
                    new()
                    {
                        Id = Guid.NewGuid(),
                        ArticuloId = Guid.NewGuid(),
                        Descripcion = "Hierro",
                        Cantidad = 12.5m,
                        UnidadMedida = "kg"
                    }
                }
            });
            await db.SaveChangesAsync();
        }

        // ...y se lee con otro, para comprobar que se persistió de verdad
        await using (var db = CrearDb(nombreBase))
        {
            var leida = await db.Licitaciones
                .Include(l => l.Items)
                .SingleAsync(l => l.LicitacionId == licitacionId);

            Assert.Equal(requisicionId, leida.RequisicionId);
            Assert.Equal("REQ-001", leida.NumeroRequisicion);
            Assert.Equal(obraId, leida.ObraId);
            Assert.Equal(EstadoLicitacion.Abierta, leida.Estado);
            Assert.Equal(fechaLimite, leida.FechaLimiteCotizacion);
            Assert.Equal(2, leida.Items.Count);

            var cemento = Assert.Single(leida.Items, i => i.Descripcion == "Cemento");
            Assert.Equal(articuloId, cemento.ArticuloId);
            Assert.Equal(80m, cemento.Cantidad);
            Assert.Equal("bolsa", cemento.UnidadMedida);
            Assert.Equal(leida.LicitacionId, cemento.LicitacionId);
        }
    }

    [Theory]
    [InlineData(EstadoLicitacion.Abierta)]
    [InlineData(EstadoLicitacion.Cerrada)]
    [InlineData(EstadoLicitacion.Adjudicada)]
    public async Task Estado_de_la_licitacion_se_guarda_y_se_lee(EstadoLicitacion estado)
    {
        var nombreBase = Guid.NewGuid().ToString();
        var id = Guid.NewGuid();

        await using (var db = CrearDb(nombreBase))
        {
            db.Licitaciones.Add(new Licitacion
            {
                LicitacionId = id,
                RequisicionId = Guid.NewGuid(),
                NumeroRequisicion = "REQ-002",
                ObraId = Guid.NewGuid(),
                Estado = estado,
                FechaCreacion = DateTime.UtcNow,
                FechaLimiteCotizacion = DateTime.UtcNow.AddDays(1)
            });
            await db.SaveChangesAsync();
        }

        await using (var db = CrearDb(nombreBase))
        {
            var leida = await db.Licitaciones.SingleAsync(l => l.LicitacionId == id);
            Assert.Equal(estado, leida.Estado);
        }
    }
}