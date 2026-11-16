using ObraSync.Recepcion.Domain.Presupuestos;
using Xunit;

namespace ObraSync.Recepcion.Tests.Domain;

public class NormalizadorEtapaTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n ")]
    public void Normalizar_CadenaNulaOVacia_DevuelveNull(string? entrada)
    {
        var resultado = NormalizadorEtapa.Normalizar(entrada);
        Assert.Null(resultado);
    }

    [Theory]
    [InlineData("  Cimientos  ", "cimientos")]
    [InlineData("ESTRUCTURA", "estructura")]
    [InlineData("Fundación", "fundacion")]
    [InlineData("FUNDACIÓN", "fundacion")]
    [InlineData("fundacion", "fundacion")]
    [InlineData("Albañilería", "albanileria")]
    [InlineData("Plano   y   Replanteo", "plano y replanteo")]
    [InlineData("  Mampostería   de Elevación  ", "mamposteria de elevacion")]
    public void Normalizar_DiferenciasDeTipeoYDiacriticos_NormalizaCorrectamente(string entrada, string esperado)
    {
        var resultado = NormalizadorEtapa.Normalizar(entrada);
        Assert.Equal(esperado, resultado);
    }

    [Theory]
    [InlineData("Cimientos", "cimientos", true)]
    [InlineData("Cimientos ", "  CIMIENTOS", true)]
    [InlineData("Fundación", "fundacion", true)]
    [InlineData("FUNDACIÓN", "Fundacion", true)]
    [InlineData("Albañilería", "albanileria", true)]
    [InlineData("Estructura", "Terminaciones", false)]
    [InlineData("Cimientos", null, false)]
    [InlineData(null, "Cimientos", false)]
    [InlineData(null, null, true)]
    [InlineData("", "   ", true)]
    public void SonEquivalentes_ComparaNormalizadoCorrectamente(string? etapaA, string? etapaB, bool esperado)
    {
        var resultado = NormalizadorEtapa.SonEquivalentes(etapaA, etapaB);
        Assert.Equal(esperado, resultado);
    }
}
