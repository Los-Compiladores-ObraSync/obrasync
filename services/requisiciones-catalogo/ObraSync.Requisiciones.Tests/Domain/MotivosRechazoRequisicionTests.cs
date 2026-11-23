using System;
using ObraSync.Requisiciones.Domain.Constants;
using Xunit;

namespace ObraSync.Requisiciones.Tests.Domain
{
    public class MotivosRechazoRequisicionTests
    {
        [Theory]
        [InlineData("Falta de presupuesto", MotivosRechazoRequisicion.FaltaPresupuesto)]
        [InlineData("  falta de presupuesto  ", MotivosRechazoRequisicion.FaltaPresupuesto)]
        [InlineData("Precio fuera de rango", MotivosRechazoRequisicion.PrecioFueraDeRango)]
        [InlineData("Cantidad incorrecta", MotivosRechazoRequisicion.CantidadIncorrecta)]
        [InlineData("Material o especificacion incorrecta", MotivosRechazoRequisicion.MaterialIncorrecto)]
        [InlineData("Material o especificación incorrecta", MotivosRechazoRequisicion.MaterialIncorrecto)]
        [InlineData("Pedido duplicado", MotivosRechazoRequisicion.PedidoDuplicado)]
        public void ResolverYValidar_ConCategoriaPermitidaSinComentario_RetornaCategoriaCanonica(
            string categoriaEntrada,
            string esperado)
        {
            var resultado = MotivosRechazoRequisicion.ResolverYValidar(
                motivo: null,
                categoriaMotivo: categoriaEntrada);

            Assert.Equal(esperado, resultado);
        }

        [Fact]
        public void ResolverYValidar_ConCategoriaPermitidaYComentarioOpcionalValido_ComponeMotivo()
        {
            var resultado = MotivosRechazoRequisicion.ResolverYValidar(
                motivo: null,
                categoriaMotivo: MotivosRechazoRequisicion.CantidadIncorrecta,
                comentario: "Pedir 20 bolsas en lugar de 80");

            Assert.Equal("Cantidad incorrecta: Pedir 20 bolsas en lugar de 80", resultado);
        }

        [Theory]
        [InlineData("Motivo inventado")]
        [InlineData("asdasdas")]
        [InlineData("Sin ganas")]
        public void ResolverYValidar_ConCategoriaFueraDeLista_LanzaArgumentException(string categoriaInvalida)
        {
            Assert.Throws<ArgumentException>(() =>
                MotivosRechazoRequisicion.ResolverYValidar(null, categoriaMotivo: categoriaInvalida));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("muy corto")]
        [InlineData("asdasdas")]
        [InlineData("asdasdasdasd")]
        [InlineData("asdfasdfasdf")]
        [InlineData("aaaaaaaaaaaa")]
        [InlineData("123456789012")]
        [InlineData("qwertyuiopas")]
        [InlineData("kjsdhfkjsdhf")]
        public void ResolverYValidar_ConCategoriaOtroYComentarioInvalidoOBasura_LanzaArgumentException(
            string? comentarioInvalido)
        {
            Assert.Throws<ArgumentException>(() =>
                MotivosRechazoRequisicion.ResolverYValidar(
                    motivo: null,
                    categoriaMotivo: MotivosRechazoRequisicion.Otro,
                    comentario: comentarioInvalido));
        }

        [Fact]
        public void ResolverYValidar_ConCategoriaOtroYComentarioValido_ComponeMotivoConPrefijoOtro()
        {
            var resultado = MotivosRechazoRequisicion.ResolverYValidar(
                motivo: null,
                categoriaMotivo: MotivosRechazoRequisicion.Otro,
                comentario: "  Se reprograma la etapa por cambio de planos  ");

            Assert.Equal("Otro: Se reprograma la etapa por cambio de planos", resultado);
        }

        [Theory]
        [InlineData("asdasdas")]
        [InlineData("asdfasdf")]
        [InlineData("qwertyuiop")]
        [InlineData("aaaaaaaaaa")]
        [InlineData("1234567890")]
        [InlineData("..........")]
        [InlineData("kjsdhfkjsdhf")]
        [InlineData("lalalalala")]
        [InlineData("asd")]
        [InlineData("Otro")]
        [InlineData("Otro: corto")]
        [InlineData("Otro: asdasdasdasd")]
        public void ResolverYValidar_EnModoLegacyConTextoBasuraOOtroIncompleto_LanzaArgumentException(string motivoBasura)
        {
            Assert.Throws<ArgumentException>(() =>
                MotivosRechazoRequisicion.ResolverYValidar(motivoBasura));
        }

        [Theory]
        [InlineData("Excede el presupuesto asignado a Estructura")]
        [InlineData("Tiene fallas en las especificaciones de construcción")]
        [InlineData("Revisar cantidades de fundaciones y columnas")]
        [InlineData("Falta de presupuesto")]
        [InlineData("Otro: Se postergó la compra hasta aprobar el cómputo")]
        public void ResolverYValidar_ConTextosRealesEnEspanol_NoProduceFalsosPositivos(string motivoReal)
        {
            var resultado = MotivosRechazoRequisicion.ResolverYValidar(motivoReal);

            Assert.False(string.IsNullOrWhiteSpace(resultado));
            Assert.False(MotivosRechazoRequisicion.EsTextoBasura(motivoReal));
        }
    }
}
