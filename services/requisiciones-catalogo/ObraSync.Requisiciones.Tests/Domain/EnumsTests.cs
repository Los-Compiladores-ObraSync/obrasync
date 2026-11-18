using System;
using ObraSync.Requisiciones.Domain.Constants;
using ObraSync.Requisiciones.Domain.Enums;
using Xunit;
using EnumEstadoRequisicion = ObraSync.Requisiciones.Domain.Enums.EstadoRequisicion;
using ConstantesEstadoRequisicion = ObraSync.Requisiciones.Domain.Constants.EstadoRequisicion;

namespace ObraSync.Requisiciones.Tests.Domain;

public class EnumsTests
{
    [Theory]
    [InlineData(EnumEstadoRequisicion.Pendiente, ConstantesEstadoRequisicion.Pendiente)]
    [InlineData(EnumEstadoRequisicion.Aprobada, ConstantesEstadoRequisicion.Aprobada)]
    [InlineData(EnumEstadoRequisicion.Rechazada, ConstantesEstadoRequisicion.Rechazada)]
    public void EstadoRequisicion_ConversionConString_MantieneEquivalenciaConConstantes(
        EnumEstadoRequisicion estadoEnum,
        string constanteEsperada)
    {
        Assert.Equal(constanteEsperada, estadoEnum.ToValorString());
        Assert.True(EstadoRequisicionExtensions.TryParse($"  {constanteEsperada.ToLowerInvariant()}  ", out var parsed));
        Assert.Equal(estadoEnum, parsed);
        Assert.Equal(estadoEnum, EstadoRequisicionExtensions.Parse(constanteEsperada));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Desconocido")]
    public void EstadoRequisicion_ValorInvalido_FallaTryParseYLanzaEnParse(string? invalido)
    {
        Assert.False(EstadoRequisicionExtensions.TryParse(invalido, out _));
        Assert.Throws<ArgumentException>(() => EstadoRequisicionExtensions.Parse(invalido));
    }

    [Theory]
    [InlineData(UnidadMedida.Bolsa, UnidadesMedida.Bolsa)]
    [InlineData(UnidadMedida.Kilogramo, UnidadesMedida.Kilogramo)]
    [InlineData(UnidadMedida.MetroCubico, UnidadesMedida.MetroCubico)]
    [InlineData(UnidadMedida.Unidad, UnidadesMedida.Unidad)]
    public void UnidadMedida_ConversionConCodigo_MantieneEquivalenciaConConstantes(
        UnidadMedida unidadEnum,
        string codigoEsperado)
    {
        Assert.Equal(codigoEsperado, unidadEnum.ToCodigo());
        Assert.True(UnidadMedidaExtensions.TryParse($" {codigoEsperado.ToLowerInvariant()} ", out var parsed));
        Assert.Equal(unidadEnum, parsed);
        Assert.Equal(unidadEnum, UnidadMedidaExtensions.Parse(codigoEsperado));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("LITRO")]
    public void UnidadMedida_ValorInvalido_FallaTryParseYLanzaEnParse(string? invalido)
    {
        Assert.False(UnidadMedidaExtensions.TryParse(invalido, out _));
        Assert.Throws<ArgumentException>(() => UnidadMedidaExtensions.Parse(invalido));
    }
}
