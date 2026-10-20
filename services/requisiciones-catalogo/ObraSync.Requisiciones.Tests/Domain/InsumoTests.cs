using System;
using ObraSync.Requisiciones.Domain.Constants;
using ObraSync.Requisiciones.Domain.Entities;
using Xunit;

namespace ObraSync.Requisiciones.Tests.Domain
{
    public class InsumoTests
    {
        [Theory]
        [InlineData(UnidadesMedida.Bolsa)]
        [InlineData(UnidadesMedida.Kilogramo)]
        [InlineData(UnidadesMedida.MetroCubico)]
        [InlineData(UnidadesMedida.Unidad)]
        public void CrearInsumo_ConUnidadValida_DebeCrearExitosamenteConMonedaUnicaARS(string unidad)
        {
            var insumo = new Insumo(Guid.NewGuid(), "MAT-TEST-001", "Insumo de prueba", unidad, true, 1500.50m);

            Assert.Equal("MAT-TEST-001", insumo.Codigo);
            Assert.Equal("Insumo de prueba", insumo.Descripcion);
            Assert.Equal(unidad, insumo.UnidadMedida);
            Assert.Equal(Monedas.PesoArgentino, insumo.Moneda);
            Assert.Equal(1500.50m, insumo.PrecioReferencia);
            Assert.True(insumo.Activo);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("LITROS")]
        [InlineData("PULGADAS")]
        public void CrearInsumo_ConUnidadInvalida_DebeLanzarArgumentException(string unidadInvalida)
        {
            Assert.Throws<ArgumentException>(() =>
                new Insumo(Guid.NewGuid(), "MAT-TEST-002", "Insumo inválido", unidadInvalida));
        }

        [Theory]
        [InlineData("USD")]
        [InlineData("EUR")]
        [InlineData("")]
        public void CrearInsumo_ConMonedaDistintaDeARS_DebeLanzarArgumentException(string monedaInvalida)
        {
            Assert.Throws<ArgumentException>(() =>
                new Insumo(Guid.NewGuid(), "MAT-TEST-003", "Insumo otra divisa", UnidadesMedida.Bolsa, true, 100m, monedaInvalida));
        }

        [Fact]
        public void CrearInsumo_ConPrecioReferenciaNegativo_DebeLanzarArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                new Insumo(Guid.NewGuid(), "MAT-TEST-004", "Insumo precio negativo", UnidadesMedida.Bolsa, true, -10m));
        }

        [Fact]
        public void CrearInsumo_ConCodigoVacio_DebeLanzarArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                new Insumo(Guid.NewGuid(), "", "Descripción válida", UnidadesMedida.Bolsa));
        }

        [Fact]
        public void DesactivarYActivar_DebeModificarEstadoCorrectamente()
        {
            var insumo = new Insumo(Guid.NewGuid(), "MAT-001", "Insumo", UnidadesMedida.Bolsa, true);

            insumo.Desactivar();
            Assert.False(insumo.Activo);

            insumo.Activar();
            Assert.True(insumo.Activo);
        }
    }
}
