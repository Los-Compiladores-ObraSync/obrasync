using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using ObraSync.Contracts.Events;
using ObraSync.Requisiciones.Application.DTOs;
using ObraSync.Requisiciones.Application.Interfaces;
using ObraSync.Requisiciones.Application.UseCases.Requisiciones.Commands;
using ObraSync.Requisiciones.Application.UseCases.Requisiciones.Handlers;
using ObraSync.Requisiciones.Domain.Constants;
using ObraSync.Requisiciones.Domain.Entities;
using Xunit;

namespace ObraSync.Requisiciones.Tests.Application
{
    public class RequisicionHandlersTests
    {
        [Fact]
        public async Task CreateRequisicion_CU02_80BolsasDeCementoParaEstructura_DebeGuardarRequisicionPendiente()
        {
            var requisicionRepo = Substitute.For<IRequisicionRepository>();
            var insumoRepo = Substitute.For<IInsumoRepository>();

            var cementoId = Guid.Parse("11111111-1111-1111-1111-111111110001");
            var insumoCemento = new Insumo(
                cementoId,
                "MAT-CEM-001",
                "Cemento Portland Normal (bolsa 50 kg)",
                UnidadesMedida.Bolsa,
                activo: true,
                precioReferencia: 8500m);

            insumoRepo.GetByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
                .Returns(new List<Insumo> { insumoCemento });

            requisicionRepo.GetNextSequenceNumberAsync(Arg.Any<CancellationToken>())
                .Returns(1);

            var handler = new CreateRequisicionCommandHandler(requisicionRepo, insumoRepo);

            var obraId = Guid.NewGuid();
            var capatazId = Guid.NewGuid();
            var command = new CreateRequisicionCommand(
                ObraId: obraId,
                SolicitanteId: capatazId,
                Items: new List<CrearRequisicionItemDto>
                {
                    new() { ArticuloId = cementoId, Cantidad = 80m }
                },
                Etapa: "Estructura",
                Observaciones: "Pedido de obra para etapa Estructura");

            var resultado = await handler.Handle(command, CancellationToken.None);

            Assert.NotEqual(Guid.Empty, resultado.RequisicionId);
            Assert.Equal($"REQ-{DateTime.UtcNow.Year}-0001", resultado.NumeroRequisicion);
            Assert.Equal(obraId, resultado.ObraId);
            Assert.Equal("Estructura", resultado.Etapa);
            Assert.Equal(capatazId, resultado.SolicitanteId);
            Assert.Equal(EstadoRequisicion.Pendiente, resultado.Estado);
            Assert.Single(resultado.Items);
            Assert.Equal(cementoId, resultado.Items[0].ArticuloId);
            Assert.Equal("Cemento Portland Normal (bolsa 50 kg)", resultado.Items[0].Descripcion);
            Assert.Equal(80m, resultado.Items[0].Cantidad);
            Assert.Equal(UnidadesMedida.Bolsa, resultado.Items[0].UnidadMedida);

            await requisicionRepo.Received(1).AddAsync(
                Arg.Is<Requisicion>(r =>
                    r.ObraId == obraId &&
                    r.Etapa == "Estructura" &&
                    r.Estado == EstadoRequisicion.Pendiente &&
                    r.Items.Count == 1 &&
                    r.Items[0].Cantidad == 80m),
                Arg.Any<CancellationToken>());
            await requisicionRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task CreateRequisicion_ConArticuloInexistente_DebeLanzarKeyNotFoundException()
        {
            var requisicionRepo = Substitute.For<IRequisicionRepository>();
            var insumoRepo = Substitute.For<IInsumoRepository>();

            insumoRepo.GetByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<Insumo>());

            var handler = new CreateRequisicionCommandHandler(requisicionRepo, insumoRepo);

            var command = new CreateRequisicionCommand(
                ObraId: Guid.NewGuid(),
                SolicitanteId: Guid.NewGuid(),
                Items: new List<CrearRequisicionItemDto>
                {
                    new() { ArticuloId = Guid.NewGuid(), Cantidad = 10m }
                });

            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                handler.Handle(command, CancellationToken.None));
        }

        [Fact]
        public async Task AprobarRequisicion_ConRequisicionPendiente_DebeAprobarYPublicarRequisicionAprobadaEvent()
        {
            var requisicionRepo = Substitute.For<IRequisicionRepository>();
            var eventPublisher = Substitute.For<IRequisicionEventPublisher>();

            var requisicionId = Guid.NewGuid();
            var obraId = Guid.NewGuid();
            var solicitanteId = Guid.NewGuid();
            var aprobadorId = Guid.NewGuid();
            var correlationId = Guid.NewGuid();
            var cementoId = Guid.Parse("11111111-1111-1111-1111-111111110001");

            var requisicion = new Requisicion(
                requisicionId,
                "REQ-2026-0001",
                obraId,
                solicitanteId,
                new[]
                {
                    new RequisicionItem(
                        Guid.NewGuid(),
                        requisicionId,
                        cementoId,
                        "Cemento Portland Normal (bolsa 50 kg)",
                        80m,
                        UnidadesMedida.Bolsa)
                },
                etapa: "Estructura");

            requisicionRepo.GetByIdAsync(requisicionId, Arg.Any<CancellationToken>())
                .Returns(requisicion);

            var handler = new AprobarRequisicionCommandHandler(requisicionRepo, eventPublisher);

            var command = new AprobarRequisicionCommand(requisicionId, aprobadorId, correlationId);
            var resultado = await handler.Handle(command, CancellationToken.None);

            Assert.Equal(EstadoRequisicion.Aprobada, resultado.Estado);
            Assert.Equal(aprobadorId, resultado.AprobadorId);
            Assert.NotNull(resultado.FechaAprobacion);

            await requisicionRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
            await eventPublisher.Received(1).PublishRequisicionAprobadaAsync(
                Arg.Is<RequisicionAprobadaEvent>(ev =>
                    ev.EventId != Guid.Empty &&
                    ev.CorrelationId == correlationId &&
                    ev.RequisicionId == requisicionId &&
                    ev.NumeroRequisicion == "REQ-2026-0001" &&
                    ev.ObraId == obraId &&
                    ev.SolicitanteId == solicitanteId &&
                    ev.AprobadorId == aprobadorId &&
                    ev.Items.Count == 1 &&
                    ev.Items[0].ArticuloId == cementoId &&
                    ev.Items[0].Cantidad == 80m &&
                    ev.Items[0].UnidadMedida == UnidadesMedida.Bolsa),
                Arg.Any<CancellationToken>());
        }
    }
}
