using System;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Domain.Presupuestos;
using Xunit;

namespace ObraSync.Recepcion.Tests.Domain;

public class EvaluadorDesvioPresupuestarioTests
{
    [Fact]
    public void Evaluar_MontoMenorAlAsignado_NoDebeBloquearse()
    {
        var partida = new PartidaPresupuestaria
        {
            MontoAsignado = 100_000m,
            MontoComprometido = 40_000m
        };
        var montoNuevaOc = 50_000m; // 40k + 50k = 90k < 100k

        var resultado = EvaluadorDesvioPresupuestario.Evaluar(partida, montoNuevaOc);

        Assert.False(resultado.DebeBloquearse);
        Assert.Equal(0m, resultado.Exceso);
        Assert.Equal(100_000m, resultado.MontoAsignado);
        Assert.Equal(40_000m, resultado.GastoAcumulado);
        Assert.Equal(50_000m, resultado.MontoOrdenCompra);
        Assert.Equal(90_000m, resultado.MontoProyectado);
        Assert.False(EvaluadorDesvioPresupuestario.DebeBloquearse(partida, montoNuevaOc));
    }

    [Fact]
    public void Evaluar_MontoExactamenteIgualAlAsignado_NoDebeBloquearse_PorReglaMayorEstricto()
    {
        // Caso borde: MontoComprometido + MontoOC == MontoAsignado
        // La regla especificada en #94 y #154 es mayor estricto (>), la igualdad no desvía.
        var partida = new PartidaPresupuestaria
        {
            MontoAsignado = 100_000m,
            MontoComprometido = 40_000m
        };
        var montoNuevaOc = 60_000m; // 40k + 60k = 100k == 100k

        var resultado = EvaluadorDesvioPresupuestario.Evaluar(partida, montoNuevaOc);

        Assert.False(resultado.DebeBloquearse);
        Assert.Equal(0m, resultado.Exceso);
        Assert.Equal(100_000m, resultado.MontoProyectado);
        Assert.Contains("coincide exactamente", resultado.Motivo);
        Assert.False(EvaluadorDesvioPresupuestario.DebeBloquearse(partida, montoNuevaOc));
    }

    [Fact]
    public void Evaluar_ExcesoMinimoDeUnCentavo_DebeBloquearse()
    {
        // Caso borde: supera por 0.01 (1 centavo)
        var partida = new PartidaPresupuestaria
        {
            MontoAsignado = 100_000m,
            MontoComprometido = 40_000m
        };
        var montoNuevaOc = 60_000.01m; // 40k + 60.000,01 = 100.000,01 > 100k

        var resultado = EvaluadorDesvioPresupuestario.Evaluar(partida, montoNuevaOc);

        Assert.True(resultado.DebeBloquearse);
        Assert.Equal(0.01m, resultado.Exceso);
        Assert.Equal(100_000.01m, resultado.MontoProyectado);
        Assert.True(EvaluadorDesvioPresupuestario.DebeBloquearse(partida, montoNuevaOc));
    }

    [Fact]
    public void Evaluar_MontoMayorAlAsignado_DebeBloquearseConExcesoCalculado()
    {
        var partida = new PartidaPresupuestaria
        {
            MontoAsignado = 100_000m,
            MontoComprometido = 30_000m
        };
        var montoNuevaOc = 85_000m; // 30k + 85k = 115k > 100k (exceso de 15k)

        var resultado = EvaluadorDesvioPresupuestario.Evaluar(partida, montoNuevaOc);

        Assert.True(resultado.DebeBloquearse);
        Assert.Equal(15_000m, resultado.Exceso);
        Assert.Equal(115_000m, resultado.MontoProyectado);
        Assert.Contains("supera la partida asignada", resultado.Motivo);
        Assert.True(EvaluadorDesvioPresupuestario.DebeBloquearse(partida, montoNuevaOc));
    }

    [Fact]
    public void Evaluar_PartidaEnCero_ConMontoMayorACero_DebeBloquearse()
    {
        // Caso borde: MontoAsignado = 0, MontoOC > 0
        var partida = new PartidaPresupuestaria
        {
            MontoAsignado = 0m,
            MontoComprometido = 0m
        };
        var montoNuevaOc = 500m;

        var resultado = EvaluadorDesvioPresupuestario.Evaluar(partida, montoNuevaOc);

        Assert.True(resultado.DebeBloquearse);
        Assert.Equal(500m, resultado.Exceso);
        Assert.Equal(500m, resultado.MontoProyectado);
        Assert.True(EvaluadorDesvioPresupuestario.DebeBloquearse(partida, montoNuevaOc));
    }

    [Fact]
    public void Evaluar_PartidaEnCero_ConMontoCero_NoDebeBloquearse()
    {
        // Caso borde: MontoAsignado = 0, MontoOC = 0 -> (0 + 0) > 0 es false
        var partida = new PartidaPresupuestaria
        {
            MontoAsignado = 0m,
            MontoComprometido = 0m
        };
        var montoNuevaOc = 0m;

        var resultado = EvaluadorDesvioPresupuestario.Evaluar(partida, montoNuevaOc);

        Assert.False(resultado.DebeBloquearse);
        Assert.Equal(0m, resultado.Exceso);
        Assert.Equal(0m, resultado.MontoProyectado);
        Assert.False(EvaluadorDesvioPresupuestario.DebeBloquearse(partida, montoNuevaOc));
    }

    [Fact]
    public void Evaluar_SinPartidaCargada_NoDebeBloquearsePreventivamente()
    {
        // Decisión #154: si no hay partida cargada para la etapa, no se bloquea preventivamente
        PartidaPresupuestaria? partida = null;
        var montoNuevaOc = 50_000m;

        var resultado = EvaluadorDesvioPresupuestario.Evaluar(partida, montoNuevaOc);

        Assert.False(resultado.DebeBloquearse);
        Assert.Equal(0m, resultado.Exceso);
        Assert.Equal(0m, resultado.MontoAsignado);
        Assert.Equal(0m, resultado.GastoAcumulado);
        Assert.Equal(50_000m, resultado.MontoOrdenCompra);
        Assert.Equal(50_000m, resultado.MontoProyectado);
        Assert.Contains("No existe partida presupuestaria", resultado.Motivo);
        Assert.False(EvaluadorDesvioPresupuestario.DebeBloquearse(partida, montoNuevaOc));
    }

    [Fact]
    public void Evaluar_MontoNegativo_LanzaArgumentOutOfRangeException()
    {
        var partida = new PartidaPresupuestaria { MontoAsignado = 100_000m };

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            EvaluadorDesvioPresupuestario.Evaluar(partida, -10m));

        Assert.Equal("montoOrdenCompra", ex.ParamName);
    }

    [Fact]
    public void Evaluar_PrecisionDecimalSinErrorDePuntoFlotante()
    {
        // Caso borde: centavos y precisión decimal estricta (no usar double)
        var partida = new PartidaPresupuestaria
        {
            MontoAsignado = 1000.99m,
            MontoComprometido = 500.50m
        };
        var montoNuevaOc = 500.49m; // 500.50 + 500.49 = 1000.99 == 1000.99

        var resultadoExacto = EvaluadorDesvioPresupuestario.Evaluar(partida, montoNuevaOc);
        Assert.False(resultadoExacto.DebeBloquearse);

        // Con 0.01 adicional supera
        var resultadoSupera = EvaluadorDesvioPresupuestario.Evaluar(partida, 500.50m); // 1001.00 > 1000.99
        Assert.True(resultadoSupera.DebeBloquearse);
        Assert.Equal(0.01m, resultadoSupera.Exceso);
    }

    [Fact]
    public void Evaluar_MontoOCIncluyeFleteSegunContrato()
    {
        // Verificación de negocio (#74, #94): El montoTotal de la OC incluye ítems más flete
        // Ítems: 950.00 + Flete: 60.00 = MontoTotal: 1010.00
        var partida = new PartidaPresupuestaria
        {
            MontoAsignado = 1000m,
            MontoComprometido = 0m
        };

        var resultado = EvaluadorDesvioPresupuestario.Evaluar(partida, 1010m);

        Assert.True(resultado.DebeBloquearse);
        Assert.Equal(10m, resultado.Exceso);
    }

    [Fact]
    public void Evaluar_PartidaYaComprometidaAlTope_CualquierMontoPositivoBloquea()
    {
        var partida = new PartidaPresupuestaria
        {
            MontoAsignado = 50_000m,
            MontoComprometido = 50_000m
        };

        var resultado = EvaluadorDesvioPresupuestario.Evaluar(partida, 0.01m);

        Assert.True(resultado.DebeBloquearse);
        Assert.Equal(0.01m, resultado.Exceso);
    }

    [Theory]
    [InlineData(1000, 200, 700, false)] // 200 + 700 = 900 <= 1000
    [InlineData(1000, 200, 800, false)] // 200 + 800 = 1000 == 1000 (igualdad)
    [InlineData(1000, 200, 800.01, true)] // 200 + 800.01 = 1000.01 > 1000
    [InlineData(500, 600, 10, true)]     // Comprometido previo ya excedido
    [InlineData(0, 0, 1, true)]          // Asignado 0 con OC positiva
    [InlineData(0, 0, 0, false)]         // Asignado 0 con OC 0
    public void Evaluar_MatrizDeCasosBorde(decimal asignado, decimal comprometido, decimal montoOc, bool debeBloquearseEsperado)
    {
        var partida = new PartidaPresupuestaria
        {
            MontoAsignado = asignado,
            MontoComprometido = comprometido
        };

        var resultado = EvaluadorDesvioPresupuestario.Evaluar(partida, montoOc);

        Assert.Equal(debeBloquearseEsperado, resultado.DebeBloquearse);
        Assert.Equal(debeBloquearseEsperado, EvaluadorDesvioPresupuestario.DebeBloquearse(partida, montoOc));
    }
}
