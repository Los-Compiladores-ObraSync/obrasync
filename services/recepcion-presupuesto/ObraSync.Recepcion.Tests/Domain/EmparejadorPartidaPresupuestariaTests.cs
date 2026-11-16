using System;
using System.Collections.Generic;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Domain.Presupuestos;
using Xunit;

namespace ObraSync.Recepcion.Tests.Domain;

public class EmparejadorPartidaPresupuestariaTests
{
    private static PartidaPresupuestaria CrearPartida(Guid obraId, string? etapa, string codigo)
    {
        return new PartidaPresupuestaria
        {
            Id = Guid.NewGuid(),
            ObraId = obraId,
            Etapa = etapa,
            CodigoPartida = codigo,
            Descripcion = $"Partida {codigo}",
            MontoAsignado = 100_000m,
            MontoComprometido = 0m,
            MontoEjecutado = 0m
        };
    }

    [Fact]
    public void BuscarPartida_ColeccionNula_DevuelveNull()
    {
        var resultado = EmparejadorPartidaPresupuestaria.BuscarPartida(null, "Cimientos");
        Assert.Null(resultado);
    }

    [Fact]
    public void BuscarPartida_ColeccionVacia_DevuelveNull()
    {
        var resultado = EmparejadorPartidaPresupuestaria.BuscarPartida(new List<PartidaPresupuestaria>(), "Cimientos");
        Assert.Null(resultado);
    }

    [Fact]
    public void BuscarPartida_CoincidenciaExacta_DevuelvePartidaDeEtapa()
    {
        // Arrange
        var obraId = Guid.NewGuid();
        var partida = CrearPartida(obraId, "Cimientos", "PART-001");
        var partidas = new List<PartidaPresupuestaria> { partida };

        // Act
        var resultado = EmparejadorPartidaPresupuestaria.BuscarPartida(partidas, "Cimientos");

        // Assert
        Assert.NotNull(resultado);
        Assert.Equal("PART-001", resultado.CodigoPartida);
    }

    [Theory]
    [InlineData("cimientos")]
    [InlineData("  CIMIENTOS  ")]
    [InlineData("Cimientos ")]
    public void BuscarPartida_DiferenciaMayusculasYEspacios_EmparejaCorrectamente(string etapaOrden)
    {
        // Arrange
        var obraId = Guid.NewGuid();
        var partida = CrearPartida(obraId, "Cimientos", "PART-001");
        var partidas = new List<PartidaPresupuestaria> { partida };

        // Act
        var resultado = EmparejadorPartidaPresupuestaria.BuscarPartida(partidas, etapaOrden);

        // Assert
        Assert.NotNull(resultado);
        Assert.Equal("PART-001", resultado.CodigoPartida);
    }

    [Theory]
    [InlineData("Fundación", "fundacion")]
    [InlineData("FUNDACION", "Fundación")]
    [InlineData("Albañilería", "albanileria")]
    public void BuscarPartida_DiferenciaTildesYDiacriticos_EmparejaCorrectamente(string etapaPartida, string etapaOrden)
    {
        // Arrange
        var obraId = Guid.NewGuid();
        var partida = CrearPartida(obraId, etapaPartida, "PART-DIAC");
        var partidas = new List<PartidaPresupuestaria> { partida };

        // Act
        var resultado = EmparejadorPartidaPresupuestaria.BuscarPartida(partidas, etapaOrden);

        // Assert
        Assert.NotNull(resultado);
        Assert.Equal("PART-DIAC", resultado.CodigoPartida);
    }

    [Fact]
    public void BuscarPartida_EtapaNoCoincidePeroExistePartidaGeneralDeObra_UsaRespaldoDeObra()
    {
        // Arrange (#196): respaldo por partida de obra completa (Etapa == null)
        var obraId = Guid.NewGuid();
        var partidaEstructura = CrearPartida(obraId, "Estructura", "PART-ESTR");
        var partidaGeneral = CrearPartida(obraId, null, "PART-GENERAL");
        var partidas = new List<PartidaPresupuestaria> { partidaEstructura, partidaGeneral };

        // Act: La orden viene con etapa "Instalaciones" que no tiene partida específica
        var resultado = EmparejadorPartidaPresupuestaria.BuscarPartida(partidas, "Instalaciones");

        // Assert: se utiliza la partida general como respaldo
        Assert.NotNull(resultado);
        Assert.Equal("PART-GENERAL", resultado.CodigoPartida);
    }

    [Fact]
    public void BuscarPartida_CoincideEtapaYExistePartidaGeneral_PrefiereLaEspecificaDeEtapa()
    {
        // Arrange
        var obraId = Guid.NewGuid();
        var partidaCimientos = CrearPartida(obraId, "Cimientos", "PART-CIM");
        var partidaGeneral = CrearPartida(obraId, null, "PART-GENERAL");
        var partidas = new List<PartidaPresupuestaria> { partidaGeneral, partidaCimientos };

        // Act
        var resultado = EmparejadorPartidaPresupuestaria.BuscarPartida(partidas, "  cimientos ");

        // Assert
        Assert.NotNull(resultado);
        Assert.Equal("PART-CIM", resultado.CodigoPartida);
    }

    [Fact]
    public void BuscarPartida_OrdenSinEtapa_UsaPartidaGeneralDeObra()
    {
        // Arrange
        var obraId = Guid.NewGuid();
        var partidaGeneral = CrearPartida(obraId, null, "PART-GENERAL");
        var partidas = new List<PartidaPresupuestaria> { partidaGeneral };

        // Act
        var resultado = EmparejadorPartidaPresupuestaria.BuscarPartida(partidas, null);

        // Assert
        Assert.NotNull(resultado);
        Assert.Equal("PART-GENERAL", resultado.CodigoPartida);
    }

    [Fact]
    public void BuscarPartida_SinPartidaDeEtapaNiGeneralDeObra_DevuelveNull()
    {
        // Arrange (#196): caso donde no existe ninguna partida aplicable
        var obraId = Guid.NewGuid();
        var partidaEstructura = CrearPartida(obraId, "Estructura", "PART-ESTR");
        var partidas = new List<PartidaPresupuestaria> { partidaEstructura };

        // Act
        var resultado = EmparejadorPartidaPresupuestaria.BuscarPartida(partidas, "Pintura");

        // Assert
        Assert.Null(resultado);
    }
}
