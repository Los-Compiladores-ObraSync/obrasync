using System;
using System.Collections.Generic;
using ObraSync.Requisiciones.Domain.Constants;
using ObraSync.Requisiciones.Domain.Entities;
using Xunit;

namespace ObraSync.Requisiciones.Tests.Domain
{
    public class RequisicionTests
    {
        [Fact]
        public void CrearRequisicion_Con80BolsasDeCementoParaEtapaEstructura_DebeQuedarEnEstadoPendiente()
        {
            var requisicionId = Guid.NewGuid();
            var obraId = Guid.NewGuid();
            var solicitanteId = Guid.NewGuid();
            var articuloCementoId = Guid.Parse("11111111-1111-1111-1111-111111110001");

            var items = new List<RequisicionItem>
            {
                new(
                    Guid.NewGuid(),
                    requisicionId,
                    articuloCementoId,
                    "Cemento Portland Normal (bolsa 50 kg)",
                    80m,
                    UnidadesMedida.Bolsa)
            };

            var requisicion = new Requisicion(
                requisicionId,
                "REQ-2026-0001",
                obraId,
                solicitanteId,
                items,
                etapa: "Estructura",
                observaciones: "Urgente para hormigonado de columnas");

            Assert.Equal(requisicionId, requisicion.Id);
            Assert.Equal("REQ-2026-0001", requisicion.NumeroRequisicion);
            Assert.Equal(obraId, requisicion.ObraId);
            Assert.Equal("Estructura", requisicion.Etapa);
            Assert.Equal(solicitanteId, requisicion.SolicitanteId);
            Assert.Equal(EstadoRequisicion.Pendiente, requisicion.Estado);
            Assert.Null(requisicion.AprobadorId);
            Assert.Null(requisicion.FechaAprobacion);
            Assert.Single(requisicion.Items);
            Assert.Equal(80m, requisicion.Items[0].Cantidad);
            Assert.Equal(UnidadesMedida.Bolsa, requisicion.Items[0].UnidadMedida);
        }

        [Fact]
        public void CrearRequisicion_SinItems_DebeLanzarArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                new Requisicion(
                    Guid.NewGuid(),
                    "REQ-2026-0002",
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Array.Empty<RequisicionItem>()));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        public void CrearRequisicionItem_ConCantidadMenorOIgualACero_DebeLanzarArgumentException(decimal cantidadInvalida)
        {
            Assert.Throws<ArgumentException>(() =>
                new RequisicionItem(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "Cemento",
                    cantidadInvalida,
                    UnidadesMedida.Bolsa));
        }

        [Fact]
        public void Aprobar_RequisicionPendiente_DebeCambiarEstadoYRegistrarAprobadorYFecha()
        {
            var requisicion = new Requisicion(
                Guid.NewGuid(),
                "REQ-2026-0003",
                Guid.NewGuid(),
                Guid.NewGuid(),
                new[]
                {
                    new RequisicionItem(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), "Cemento", 80m, UnidadesMedida.Bolsa)
                },
                "Estructura");

            var aprobadorId = Guid.NewGuid();
            requisicion.Aprobar(aprobadorId);

            Assert.Equal(EstadoRequisicion.Aprobada, requisicion.Estado);
            Assert.Equal(aprobadorId, requisicion.AprobadorId);
            Assert.NotNull(requisicion.FechaAprobacion);
        }

        [Fact]
        public void Aprobar_RequisicionYaAprobada_DebeLanzarInvalidOperationException()
        {
            var requisicion = new Requisicion(
                Guid.NewGuid(),
                "REQ-2026-0004",
                Guid.NewGuid(),
                Guid.NewGuid(),
                new[]
                {
                    new RequisicionItem(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), "Cemento", 80m, UnidadesMedida.Bolsa)
                });

            requisicion.Aprobar(Guid.NewGuid());

            Assert.Throws<InvalidOperationException>(() =>
                requisicion.Aprobar(Guid.NewGuid()));
        }
    }
}
