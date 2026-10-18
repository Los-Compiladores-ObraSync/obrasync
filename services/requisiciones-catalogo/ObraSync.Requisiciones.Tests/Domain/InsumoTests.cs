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
        public void CrearInsumo_ConUnidadValida_DebeCrearExitosamente(string unidad)
        {
            var insumo = new Insumo(Guid.NewGuid(), "MAT-TEST-001", "Insumo de prueba", unidad, true);

            Assert.Equal("MAT-TEST-001", insumo.Codigo);
            Assert.Equal("Insumo de prueba", insumo.Descripcion);
            Assert.Equal(unidad, insumo.UnidadMedida);
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
