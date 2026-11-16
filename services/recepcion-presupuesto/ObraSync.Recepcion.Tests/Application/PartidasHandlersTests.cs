using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Exceptions;
using ObraSync.Recepcion.Application.UseCases.Partidas.Commands;
using ObraSync.Recepcion.Application.UseCases.Partidas.Handlers;
using ObraSync.Recepcion.Application.UseCases.Partidas.Queries;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Infrastructure.Persistence;
using Xunit;

namespace ObraSync.Recepcion.Tests.Application;

public class PartidasHandlersTests
{
    private static RecepcionDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<RecepcionDbContext>()
            .UseInMemoryDatabase($"Partidas_{Guid.NewGuid()}")
            .Options;
        return new RecepcionDbContext(options);
    }

    [Fact]
    public async Task CrearPartida_DatosValidos_GuardaYDevuelveDto()
    {
        using var db = CreateInMemoryContext();
        var handler = new CrearPartidaPresupuestariaCommandHandler(db);
        var obraId = Guid.NewGuid();

        var request = new CrearPartidaPresupuestariaRequest
        {
            ObraId = obraId,
            Etapa = "Estructura",
            CodigoPartida = "EST-01",
            Descripcion = "Hormigón y armaduras",
            MontoAsignado = 500_000m,
            Moneda = "ARS"
        };

        var result = await handler.Handle(new CrearPartidaPresupuestariaCommand(request));

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(obraId, result.ObraId);
        Assert.Equal("Estructura", result.Etapa);
        Assert.Equal("EST-01", result.CodigoPartida);
        Assert.Equal("Hormigón y armaduras", result.Descripcion);
        Assert.Equal(500_000m, result.MontoAsignado);
        Assert.Equal(0m, result.MontoComprometido);
        Assert.Equal(0m, result.MontoEjecutado);
        Assert.Equal(500_000m, result.MontoDisponible);
        Assert.Equal("ARS", result.Moneda);

        var enDb = await db.PartidasPresupuestarias.FindAsync(result.Id);
        Assert.NotNull(enDb);
    }

    [Theory]
    [InlineData("", "EST-01", "Desc", 1000)]
    [InlineData("codigo", "", "Desc", 1000)]
    [InlineData("codigo", "EST-01", "", 1000)]
    [InlineData("codigo", "EST-01", "Desc", -10)]
    public async Task CrearPartida_DatosInvalidos_LanzaArgumentException(
        string campoInvalido,
        string codigo,
        string descripcion,
        decimal monto)
    {
        using var db = CreateInMemoryContext();
        var handler = new CrearPartidaPresupuestariaCommandHandler(db);

        var request = new CrearPartidaPresupuestariaRequest
        {
            ObraId = campoInvalido == "" ? Guid.Empty : Guid.NewGuid(),
            CodigoPartida = codigo,
            Descripcion = descripcion,
            MontoAsignado = monto
        };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new CrearPartidaPresupuestariaCommand(request)));
    }

    [Fact]
    public async Task CrearPartida_CodigoDuplicadoMismaObra_LanzaRecepcionConflictoException()
    {
        using var db = CreateInMemoryContext();
        var handler = new CrearPartidaPresupuestariaCommandHandler(db);
        var obraId = Guid.NewGuid();

        var request1 = new CrearPartidaPresupuestariaRequest
        {
            ObraId = obraId,
            CodigoPartida = "EST-01",
            Descripcion = "Primera partida",
            MontoAsignado = 100_000m
        };

        var request2 = new CrearPartidaPresupuestariaRequest
        {
            ObraId = obraId,
            CodigoPartida = "EST-01", // Mismo código
            Descripcion = "Segunda partida",
            MontoAsignado = 200_000m
        };

        await handler.Handle(new CrearPartidaPresupuestariaCommand(request1));

        var ex = await Assert.ThrowsAsync<RecepcionConflictoException>(() =>
            handler.Handle(new CrearPartidaPresupuestariaCommand(request2)));

        Assert.Contains("EST-01", ex.Message);
    }

    [Fact]
    public async Task CrearPartida_EtapaDuplicadaMismaObra_LanzaRecepcionConflictoException()
    {
        using var db = CreateInMemoryContext();
        var handler = new CrearPartidaPresupuestariaCommandHandler(db);
        var obraId = Guid.NewGuid();

        var request1 = new CrearPartidaPresupuestariaRequest
        {
            ObraId = obraId,
            Etapa = "Estructura",
            CodigoPartida = "EST-01",
            Descripcion = "Primera partida de estructura",
            MontoAsignado = 100_000m
        };

        var request2 = new CrearPartidaPresupuestariaRequest
        {
            ObraId = obraId,
            Etapa = "  estructura  ", // Distinto código pero misma etapa (case-insensitive)
            CodigoPartida = "EST-02",
            Descripcion = "Segunda partida de estructura",
            MontoAsignado = 200_000m
        };

        await handler.Handle(new CrearPartidaPresupuestariaCommand(request1));

        var ex = await Assert.ThrowsAsync<RecepcionConflictoException>(() =>
            handler.Handle(new CrearPartidaPresupuestariaCommand(request2)));

        Assert.Contains("estructura", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CrearPartida_AmbasSinEtapaMismaObra_LanzaRecepcionConflictoException()
    {
        using var db = CreateInMemoryContext();
        var handler = new CrearPartidaPresupuestariaCommandHandler(db);
        var obraId = Guid.NewGuid();

        var request1 = new CrearPartidaPresupuestariaRequest
        {
            ObraId = obraId,
            Etapa = null,
            CodigoPartida = "GEN-01",
            Descripcion = "Partida general 1",
            MontoAsignado = 500_000m
        };

        var request2 = new CrearPartidaPresupuestariaRequest
        {
            ObraId = obraId,
            Etapa = "   ", // Equivale a Etapa = null ("toda la obra")
            CodigoPartida = "GEN-02",
            Descripcion = "Partida general 2",
            MontoAsignado = 300_000m
        };

        await handler.Handle(new CrearPartidaPresupuestariaCommand(request1));

        var ex = await Assert.ThrowsAsync<RecepcionConflictoException>(() =>
            handler.Handle(new CrearPartidaPresupuestariaCommand(request2)));

        Assert.Contains("toda la obra", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(2601)]
    [InlineData(2627)]
    public async Task CrearPartida_ViolacionIndiceUnicoEnSaveChanges_LanzaRecepcionConflictoException(int sqlErrorNumber)
    {
        using var db = new ConcurrenciaIndiceUnicoDbContext(
            new DbContextOptionsBuilder<RecepcionDbContext>()
                .UseInMemoryDatabase($"Partidas_Unique_{Guid.NewGuid()}")
                .Options,
            sqlErrorNumber);

        var handler = new CrearPartidaPresupuestariaCommandHandler(db);
        var request = new CrearPartidaPresupuestariaRequest
        {
            ObraId = Guid.NewGuid(),
            Etapa = "Cimientos",
            CodigoPartida = "CIM-01",
            Descripcion = "Partida concurrente",
            MontoAsignado = 100_000m
        };

        await Assert.ThrowsAsync<RecepcionConflictoException>(() =>
            handler.Handle(new CrearPartidaPresupuestariaCommand(request)));
    }

    [Fact]
    public async Task ObtenerPartidas_FiltraPorObraYEtapa_DevuelveCorrectamente()
    {
        using var db = CreateInMemoryContext();
        var obra1 = Guid.NewGuid();
        var obra2 = Guid.NewGuid();

        db.PartidasPresupuestarias.AddRange(
            new PartidaPresupuestaria { Id = Guid.NewGuid(), ObraId = obra1, CodigoPartida = "A", Descripcion = "A", Etapa = "Estructura", MontoAsignado = 100 },
            new PartidaPresupuestaria { Id = Guid.NewGuid(), ObraId = obra1, CodigoPartida = "B", Descripcion = "B", Etapa = "Terminaciones", MontoAsignado = 200 },
            new PartidaPresupuestaria { Id = Guid.NewGuid(), ObraId = obra2, CodigoPartida = "C", Descripcion = "C", Etapa = "Estructura", MontoAsignado = 300 }
        );
        await db.SaveChangesAsync();

        var handler = new ObtenerPartidasQueryHandler(db);

        // Sin filtro -> Todas (3)
        var todas = await handler.Handle(new ObtenerPartidasQuery());
        Assert.Equal(3, todas.Count);

        // Filtrar por obra1 -> 2
        var deObra1 = await handler.Handle(new ObtenerPartidasQuery(ObraId: obra1));
        Assert.Equal(2, deObra1.Count);

        // Filtrar por obra1 y etapa Estructura -> 1
        var obra1Estructura = await handler.Handle(new ObtenerPartidasQuery(ObraId: obra1, Etapa: "estructura"));
        Assert.Single(obra1Estructura);
        Assert.Equal("A", obra1Estructura[0].CodigoPartida);
    }

    [Fact]
    public async Task ObtenerPartidaPorId_ExistenteEInexistente()
    {
        using var db = CreateInMemoryContext();
        var id = Guid.NewGuid();
        db.PartidasPresupuestarias.Add(new PartidaPresupuestaria
        {
            Id = id,
            ObraId = Guid.NewGuid(),
            CodigoPartida = "PART-1",
            Descripcion = "Test",
            MontoAsignado = 50_000m
        });
        await db.SaveChangesAsync();

        var handler = new ObtenerPartidaPorIdQueryHandler(db);

        var encontrada = await handler.Handle(new ObtenerPartidaPorIdQuery(id));
        Assert.NotNull(encontrada);
        Assert.Equal("PART-1", encontrada.CodigoPartida);

        var inexistente = await handler.Handle(new ObtenerPartidaPorIdQuery(Guid.NewGuid()));
        Assert.Null(inexistente);
    }

    private sealed class SqlException : Exception
    {
        public SqlException(int number) : base($"Violation of UNIQUE KEY constraint ({number}).")
        {
            Number = number;
        }

        public int Number { get; }
    }

    private sealed class ConcurrenciaIndiceUnicoDbContext : RecepcionDbContext
    {
        private readonly int _sqlErrorNumber;

        public ConcurrenciaIndiceUnicoDbContext(
            DbContextOptions<RecepcionDbContext> options,
            int sqlErrorNumber)
            : base(options)
        {
            _sqlErrorNumber = sqlErrorNumber;
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            throw new DbUpdateException("Error al guardar por índice único.", new SqlException(_sqlErrorNumber));
        }
    }
}
