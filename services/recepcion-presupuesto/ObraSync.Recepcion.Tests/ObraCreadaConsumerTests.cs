using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using ObraSync.Contracts.Events;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Infrastructure.Configuration;
using ObraSync.Recepcion.Infrastructure.Consumers;
using ObraSync.Recepcion.Infrastructure.Persistence;
using Xunit;

namespace ObraSync.Recepcion.Tests;

public class ObraCreadaConsumerTests
{
    private static RecepcionDbContext Db() =>
        new(new DbContextOptionsBuilder<RecepcionDbContext>().UseInMemoryDatabase($"ObraCreada_{Guid.NewGuid()}").Options);

    private static async Task Consumir(RecepcionDbContext db, ObraCreadaEvent evento, decimal monto = 1_000_000m)
    {
        var consumer = new ObraCreadaConsumer(db, NullLogger<ObraCreadaConsumer>.Instance,
            Options.Create(new PresupuestoSemillaOptions { MontoSemillaPorEtapa = monto }));
        var ctx = Substitute.For<MassTransit.ConsumeContext<ObraCreadaEvent>>();
        ctx.Message.Returns(evento);
        ctx.CancellationToken.Returns(CancellationToken.None);
        await consumer.Consume(ctx);
    }

    private static ObraCreadaEvent Evento(Guid obraId, params string[] etapas) =>
        new(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), obraId, "Obra", etapas);

    [Fact]
    public async Task CreaUnaPartidaSemillaPorEtapa()
    {
        using var db = Db();
        var obra = Guid.NewGuid();

        await Consumir(db, Evento(obra, "Fundaciones", "Estructura"));

        var partidas = await db.PartidasPresupuestarias.Where(p => p.ObraId == obra).ToListAsync();
        Assert.Equal(2, partidas.Count);
        Assert.All(partidas, p => Assert.Equal(1_000_000m, p.MontoAsignado));
    }

    [Fact]
    public async Task RepublicarLaObraNoDuplicaNiPisaPartidasExistentes()
    {
        using var db = Db();
        var obra = Guid.NewGuid();

        await Consumir(db, Evento(obra, "Fundaciones"));
        var existente = await db.PartidasPresupuestarias.SingleAsync();
        existente.ComprometerMonto(300_000m);
        await db.SaveChangesAsync();

        await Consumir(db, Evento(obra, "fundaciones ", "Techos"));

        var partidas = await db.PartidasPresupuestarias.ToListAsync();
        Assert.Equal(2, partidas.Count);
        Assert.Equal(300_000m, partidas.Single(p => p.Etapa == "Fundaciones").MontoComprometido);
    }

    [Fact]
    public async Task SinEtapasCreaUnaPartidaGeneral_YConMontoCeroNoHaceNada()
    {
        using var db = Db();
        var obra = Guid.NewGuid();

        await Consumir(db, Evento(obra));
        Assert.Null(Assert.Single(db.PartidasPresupuestarias).Etapa);

        using var db2 = Db();
        await Consumir(db2, Evento(Guid.NewGuid(), "Techos"), monto: 0m);
        Assert.Empty(db2.PartidasPresupuestarias);
    }
}
