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
                    "Cemento Portland Normal (bolsa 25 kg)",
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
            Assert.Null(requisicion.RechazadorId);
            Assert.Null(requisicion.MotivoRechazo);
            Assert.Null(requisicion.FechaRechazo);
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

        private static Requisicion NuevaPendiente() => new(
            Guid.NewGuid(),
            "REQ-2026-0090",
            Guid.NewGuid(),
            Guid.NewGuid(),
            new[]
            {
                new RequisicionItem(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), "Cemento", 80m, UnidadesMedida.Bolsa)
            },
            "Estructura");

        [Fact]
        public void Cancelar_RequisicionPendiente_PasaACancelada()
        {
            var requisicion = NuevaPendiente();

            requisicion.Cancelar();

            Assert.Equal(EstadoRequisicion.Cancelada, requisicion.Estado);
        }

        [Fact]
        public void Cancelar_RequisicionAprobadaOCancelada_LanzaInvalidOperationException()
        {
            var aprobada = NuevaPendiente();
            aprobada.Aprobar(Guid.NewGuid());
            Assert.Throws<InvalidOperationException>(() => aprobada.Cancelar());

            var cancelada = NuevaPendiente();
            cancelada.Cancelar();
            Assert.Throws<InvalidOperationException>(() => cancelada.Cancelar());
        }

        [Fact]
        public void Reenviar_RequisicionPendiente_CambiaLosDatosYSigueSiendoPendiente()
        {
            var requisicion = NuevaPendiente();
            var nuevoArticulo = Guid.NewGuid();

            requisicion.Reenviar(
                new[] { new RequisicionItem(Guid.NewGuid(), requisicion.Id, nuevoArticulo, "Cal", 15m, UnidadesMedida.Bolsa) },
                "Terminaciones",
                "Corregida");

            Assert.Equal(EstadoRequisicion.Pendiente, requisicion.Estado);
            Assert.Equal("Terminaciones", requisicion.Etapa);
            var item = Assert.Single(requisicion.Items);
            Assert.Equal(nuevoArticulo, item.ArticuloId);
            Assert.Empty(requisicion.HistorialRechazos);
        }

        [Fact]
        public void Reenviar_RequisicionAprobadaOCancelada_LanzaInvalidOperationException()
        {
            var items = new[] { new RequisicionItem(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), "Cal", 15m, UnidadesMedida.Bolsa) };

            var aprobada = NuevaPendiente();
            aprobada.Aprobar(Guid.NewGuid());
            Assert.Throws<InvalidOperationException>(() => aprobada.Reenviar(items));

            var cancelada = NuevaPendiente();
            cancelada.Cancelar();
            Assert.Throws<InvalidOperationException>(() => cancelada.Reenviar(items));
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

        [Fact]
        public void Rechazar_RequisicionPendiente_DebeCambiarEstadoARechazadaYGuardarRechazadorMotivoYFecha()
        {
            var requisicion = new Requisicion(
                Guid.NewGuid(),
                "REQ-2026-0005",
                Guid.NewGuid(),
                Guid.NewGuid(),
                new[]
                {
                    new RequisicionItem(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), "Cemento", 80m, UnidadesMedida.Bolsa)
                },
                "Estructura");

            var rechazadorId = Guid.NewGuid();
            var fechaRechazo = new DateTime(2026, 10, 25, 15, 30, 0, DateTimeKind.Utc);

            requisicion.Rechazar(rechazadorId, "  Presupuesto excedido para etapa Estructura  ", fechaRechazo);

            Assert.Equal(EstadoRequisicion.Rechazada, requisicion.Estado);
            Assert.Equal(rechazadorId, requisicion.RechazadorId);
            Assert.Equal("Presupuesto excedido para etapa Estructura", requisicion.MotivoRechazo);
            Assert.Equal(fechaRechazo, requisicion.FechaRechazo);
            Assert.Null(requisicion.AprobadorId);
            Assert.Null(requisicion.FechaAprobacion);
        }

        [Fact]
        public void Rechazar_RequisicionYaAprobadaORechazada_DebeLanzarInvalidOperationException()
        {
            var reqAprobada = new Requisicion(
                Guid.NewGuid(),
                "REQ-2026-0006",
                Guid.NewGuid(),
                Guid.NewGuid(),
                new[]
                {
                    new RequisicionItem(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), "Cemento", 80m, UnidadesMedida.Bolsa)
                });

            reqAprobada.Aprobar(Guid.NewGuid());

            Assert.Throws<InvalidOperationException>(() =>
                reqAprobada.Rechazar(Guid.NewGuid(), "No se puede rechazar luego de aprobada"));

            var reqRechazada = new Requisicion(
                Guid.NewGuid(),
                "REQ-2026-0007",
                Guid.NewGuid(),
                Guid.NewGuid(),
                new[]
                {
                    new RequisicionItem(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), "Cemento", 80m, UnidadesMedida.Bolsa)
                });

            reqRechazada.Rechazar(Guid.NewGuid(), "Primer rechazo");

            Assert.Throws<InvalidOperationException>(() =>
                reqRechazada.Rechazar(Guid.NewGuid(), "Segundo rechazo"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Rechazar_ConMotivoVacioOSoloEspacios_DebeLanzarArgumentException(string motivoInvalido)
        {
            var requisicion = new Requisicion(
                Guid.NewGuid(),
                "REQ-2026-0008",
                Guid.NewGuid(),
                Guid.NewGuid(),
                new[]
                {
                    new RequisicionItem(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), "Cemento", 80m, UnidadesMedida.Bolsa)
                });

            Assert.Throws<ArgumentException>(() =>
                requisicion.Rechazar(Guid.NewGuid(), motivoInvalido));
        }

        [Fact]
        public void Rechazar_ConRechazadorIdVacioOMotivoMayorA500Caracteres_DebeLanzarArgumentException()
        {
            var requisicion = new Requisicion(
                Guid.NewGuid(),
                "REQ-2026-0009",
                Guid.NewGuid(),
                Guid.NewGuid(),
                new[]
                {
                    new RequisicionItem(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), "Cemento", 80m, UnidadesMedida.Bolsa)
                });

            Assert.Throws<ArgumentException>(() =>
                requisicion.Rechazar(Guid.Empty, "Motivo válido"));

            var motivoLargo = new string('A', 501);
            Assert.Throws<ArgumentException>(() =>
                requisicion.Rechazar(Guid.NewGuid(), motivoLargo));
        }

        [Fact]
        public void RequisicionItemCrearDesde_InsumoActivo_CopiaDatosYInactivoLanzaArgumentException()
        {
            var requisicionId = Guid.NewGuid();
            var activo = new Insumo(Guid.NewGuid(), "MAT-CEM-001", "Cemento Portland", UnidadesMedida.Bolsa, activo: true);
            var inactivo = new Insumo(Guid.NewGuid(), "MAT-OBS-001", "Insumo discontinuado", UnidadesMedida.Bolsa, activo: false);

            var item = RequisicionItem.CrearDesde(requisicionId, activo, 25m);

            Assert.Equal(requisicionId, item.RequisicionId);
            Assert.Equal(activo.Id, item.ArticuloId);
            Assert.Equal("Cemento Portland", item.Descripcion);
            Assert.Equal(25m, item.Cantidad);
            Assert.Equal(UnidadesMedida.Bolsa, item.UnidadMedida);

            Assert.Throws<ArgumentException>(() => RequisicionItem.CrearDesde(requisicionId, inactivo, 10m));
        }

        [Fact]
        public void FormatearNumeroYNormalizarIdempotencyKey_AplicanReglasDeDominio()
        {
            Assert.Equal("REQ-2026-0007", Requisicion.FormatearNumero(2026, 7));
            Assert.Throws<ArgumentOutOfRangeException>(() => Requisicion.FormatearNumero(2026, 0));

            Assert.Null(Requisicion.NormalizarIdempotencyKey("   "));
            Assert.Equal("clave-1", Requisicion.NormalizarIdempotencyKey("  clave-1  "));
            Assert.Throws<ArgumentException>(() => Requisicion.NormalizarIdempotencyKey(new string('x', 101)));
        }

        [Fact]
        public void AsegurarPuedeSerModificadaPor_PermiteDuenoOAdminYRechazaATerceros()
        {
            var duenoId = Guid.NewGuid();
            var otroId = Guid.NewGuid();
            var requisicion = new Requisicion(
                Guid.NewGuid(),
                "REQ-2026-0010",
                Guid.NewGuid(),
                duenoId,
                new[] { new RequisicionItem(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), "Cemento", 10m, UnidadesMedida.Bolsa) });

            requisicion.AsegurarPuedeSerModificadaPor(duenoId, esAdmin: false, "cancelarla");
            requisicion.AsegurarPuedeSerModificadaPor(otroId, esAdmin: true, "cancelarla");

            Assert.Throws<UnauthorizedAccessException>(() =>
                requisicion.AsegurarPuedeSerModificadaPor(otroId, esAdmin: false, "cancelarla"));
        }

        [Fact]
        public void NormalizarObservaciones_ConHasta1000CaracteresRecortaYConMasDe1000LanzaArgumentException()
        {
            var alLimite = new string('A', Requisicion.LongitudMaximaObservaciones);
            var requisicion = new Requisicion(
                Guid.NewGuid(),
                "REQ-2026-0011",
                Guid.NewGuid(),
                Guid.NewGuid(),
                new[] { new RequisicionItem(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), "Cemento", 10m, UnidadesMedida.Bolsa) },
                observaciones: $"  {alLimite}  ");

            Assert.Equal(alLimite, requisicion.Observaciones);

            var excedida = new string('B', Requisicion.LongitudMaximaObservaciones + 1);
            var exCreacion = Assert.Throws<ArgumentException>(() =>
                new Requisicion(
                    Guid.NewGuid(),
                    "REQ-2026-0012",
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    new[] { new RequisicionItem(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), "Cemento", 10m, UnidadesMedida.Bolsa) },
                    observaciones: excedida));
            Assert.Contains(Requisicion.LongitudMaximaObservaciones.ToString(), exCreacion.Message);

            requisicion.Reenviar(
                new[] { new RequisicionItem(Guid.NewGuid(), requisicion.Id, Guid.NewGuid(), "Cal", 5m, UnidadesMedida.Bolsa) },
                observaciones: alLimite);
            Assert.Equal(alLimite, requisicion.Observaciones);

            var exReenvio = Assert.Throws<ArgumentException>(() =>
                requisicion.Reenviar(
                    new[] { new RequisicionItem(Guid.NewGuid(), requisicion.Id, Guid.NewGuid(), "Cal", 5m, UnidadesMedida.Bolsa) },
                    observaciones: excedida));
            Assert.Contains(Requisicion.LongitudMaximaObservaciones.ToString(), exReenvio.Message);
        }
    }
}
