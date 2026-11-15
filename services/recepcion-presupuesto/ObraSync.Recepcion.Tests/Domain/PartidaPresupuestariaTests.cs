using System;
using ObraSync.Recepcion.Domain.Entities;
using Xunit;

namespace ObraSync.Recepcion.Tests.Domain;

public class PartidaPresupuestariaTests
{
    [Fact]
    public void MontoDisponible_CalculaDiferenciaEntreAsignadoYComprometido()
    {
        var partida = new PartidaPresupuestaria
        {
            MontoAsignado = 100_000m,
            MontoComprometido = 40_000m
        };

        Assert.Equal(60_000m, partida.MontoDisponible);
    }

    [Theory]
    [InlineData(100_000, 40_000, 50_000, false)] // 40k + 50k = 90k <= 100k
    [InlineData(100_000, 40_000, 60_000, false)] // 40k + 60k = 100k == 100k (no supera, igualdad estricta #154)
    [InlineData(100_000, 40_000, 60_000.01, true)] // 40k + 60.000,01 = 100.000,01 > 100k (supera)
    [InlineData(100_000, 100_000, 1, true)]       // Totalmente comprometida
    public void SuperaPartida_EvaluaReglaCorrectamente(decimal asignado, decimal comprometido, decimal nuevaOc, bool esperado)
    {
        var partida = new PartidaPresupuestaria
        {
            MontoAsignado = asignado,
            MontoComprometido = comprometido
        };

        var resultado = partida.SuperaPartida(nuevaOc);

        Assert.Equal(esperado, resultado);
    }

    [Fact]
    public void SuperaPartida_MontoNegativo_LanzaExcepcion()
    {
        var partida = new PartidaPresupuestaria { MontoAsignado = 10_000m };

        Assert.Throws<ArgumentOutOfRangeException>(() => partida.SuperaPartida(-1m));
    }

    [Fact]
    public void ComprometerMonto_IncrementaMontoComprometido()
    {
        var partida = new PartidaPresupuestaria
        {
            MontoAsignado = 100_000m,
            MontoComprometido = 10_000m
        };

        partida.ComprometerMonto(25_000m);

        Assert.Equal(35_000m, partida.MontoComprometido);
        Assert.Equal(65_000m, partida.MontoDisponible);
    }

    [Fact]
    public void ComprometerMonto_MontoNegativo_LanzaExcepcion()
    {
        var partida = new PartidaPresupuestaria();

        Assert.Throws<ArgumentOutOfRangeException>(() => partida.ComprometerMonto(-500m));
    }

    [Fact]
    public void EjecutarMonto_IncrementaMontoEjecutado()
    {
        var partida = new PartidaPresupuestaria
        {
            MontoAsignado = 100_000m,
            MontoEjecutado = 5_000m
        };

        partida.EjecutarMonto(12_500m);

        Assert.Equal(17_500m, partida.MontoEjecutado);
    }

    [Fact]
    public void EjecutarMonto_MontoNegativo_LanzaExcepcion()
    {
        var partida = new PartidaPresupuestaria();

        Assert.Throws<ArgumentOutOfRangeException>(() => partida.EjecutarMonto(-100m));
    }

    [Fact]
    public void LiberarComprometido_ReduceComprometidoSinBajarDeCero()
    {
        var partida = new PartidaPresupuestaria
        {
            MontoAsignado = 100_000m,
            MontoComprometido = 20_000m
        };

        partida.LiberarComprometido(5_000m);
        Assert.Equal(15_000m, partida.MontoComprometido);

        partida.LiberarComprometido(30_000m);
        Assert.Equal(0m, partida.MontoComprometido);
    }

    [Fact]
    public void LiberarComprometido_MontoNegativo_LanzaExcepcion()
    {
        var partida = new PartidaPresupuestaria();

        Assert.Throws<ArgumentOutOfRangeException>(() => partida.LiberarComprometido(-10m));
    }
}
