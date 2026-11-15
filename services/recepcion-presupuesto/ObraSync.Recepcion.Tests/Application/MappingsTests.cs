using System;
using System.Collections.Generic;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Mappings;
using ObraSync.Recepcion.Domain.Enums;
using RecepcionEntity = ObraSync.Recepcion.Domain.Entities.Recepcion;
using DetalleRecepcionEntity = ObraSync.Recepcion.Domain.Entities.DetalleRecepcion;
using PartidaPresupuestariaEntity = ObraSync.Recepcion.Domain.Entities.PartidaPresupuestaria;
using Xunit;

namespace ObraSync.Recepcion.Tests.Application;

public class MappingsTests
{
    [Fact]
    public void PartidaPresupuestaria_ToDto_MapeaTodasLasPropiedadesCorrectamente()
    {
        var id = Guid.NewGuid();
        var obraId = Guid.NewGuid();
        var fecha = DateTime.UtcNow;

        var partida = new PartidaPresupuestariaEntity
        {
            Id = id,
            ObraId = obraId,
            Etapa = "Estructura",
            CodigoPartida = "PART-001",
            Descripcion = "Hormigón elaborado",
            MontoAsignado = 150_000m,
            MontoComprometido = 50_000m,
            MontoEjecutado = 20_000m,
            Moneda = "ARS",
            CreatedAt = fecha
        };

        var dto = partida.ToDto();

        Assert.Equal(id, dto.Id);
        Assert.Equal(obraId, dto.ObraId);
        Assert.Equal("Estructura", dto.Etapa);
        Assert.Equal("PART-001", dto.CodigoPartida);
        Assert.Equal("Hormigón elaborado", dto.Descripcion);
        Assert.Equal(150_000m, dto.MontoAsignado);
        Assert.Equal(50_000m, dto.MontoComprometido);
        Assert.Equal(20_000m, dto.MontoEjecutado);
        Assert.Equal(100_000m, dto.MontoDisponible);
        Assert.Equal("ARS", dto.Moneda);
        Assert.Equal(fecha, dto.CreatedAt);
    }

    [Fact]
    public void PartidaPresupuestaria_ToDto_Null_LanzaArgumentNullException()
    {
        PartidaPresupuestariaEntity? partida = null;
        Assert.Throws<ArgumentNullException>(() => partida!.ToDto());
    }

    [Fact]
    public void Recepcion_ToDto_MapeaEntidadYDetallesConFechasUtc()
    {
        var id = Guid.NewGuid();
        var obraId = Guid.NewGuid();
        var ordenCompraId = Guid.NewGuid();
        var usuarioId = Guid.NewGuid();
        var fechaRecepcion = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Unspecified);
        var createdAt = new DateTime(2026, 11, 15, 10, 30, 0, DateTimeKind.Local);

        var recepcion = new RecepcionEntity
        {
            Id = id,
            ObraId = obraId,
            OrdenCompraId = ordenCompraId,
            RecepcionadoPorUsuarioId = usuarioId,
            NumeroRemito = "REM-001",
            FechaRecepcion = fechaRecepcion,
            Estado = EstadoRecepcion.Completa,
            Observaciones = "Todo recibido conforme",
            CreatedAt = createdAt,
            UpdatedAt = null,
            Detalles = new List<DetalleRecepcionEntity>
            {
                new DetalleRecepcionEntity
                {
                    Id = Guid.NewGuid(),
                    RecepcionId = id,
                    ArticuloId = Guid.NewGuid(),
                    DescripcionArticulo = "Bolsa de Cemento",
                    CantidadEsperada = 100,
                    CantidadRecibida = 100,
                    UnidadMedida = "bolsa",
                    Conforme = true,
                    CreatedAt = createdAt
                }
            }
        };

        var dto = recepcion.ToDto();

        Assert.Equal(id, dto.Id);
        Assert.Equal(obraId, dto.ObraId);
        Assert.Equal(ordenCompraId, dto.OrdenCompraId);
        Assert.Equal(usuarioId, dto.RecepcionadoPorUsuarioId);
        Assert.Equal("REM-001", dto.NumeroRemito);
        Assert.Equal(DateTimeKind.Utc, dto.FechaRecepcion.Kind);
        Assert.Equal(DateTimeKind.Utc, dto.CreatedAt.Kind);
        Assert.Null(dto.UpdatedAt);
        Assert.Single(dto.Detalles);

        var detalleDto = dto.Detalles[0];
        Assert.Equal("Bolsa de Cemento", detalleDto.DescripcionArticulo);
        Assert.Equal(100m, detalleDto.CantidadRecibida);
        Assert.True(detalleDto.Conforme);
        Assert.Equal(DateTimeKind.Utc, detalleDto.CreatedAt.Kind);
    }

    [Fact]
    public void RecepcionDto_DesdeEntidad_MantieneCompatibilidadHaciaAtras()
    {
        var recepcion = new RecepcionEntity
        {
            Id = Guid.NewGuid(),
            NumeroRemito = "REM-TEST",
            FechaRecepcion = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        var dtoCompat = RecepcionDto.DesdeEntidad(recepcion);
        var dtoExtension = recepcion.ToDto();

        Assert.Equal(dtoCompat.Id, dtoExtension.Id);
        Assert.Equal(dtoCompat.NumeroRemito, dtoExtension.NumeroRemito);
    }

    [Fact]
    public void Recepcion_ToDto_Null_LanzaArgumentNullException()
    {
        RecepcionEntity? recepcion = null;
        Assert.Throws<ArgumentNullException>(() => recepcion!.ToDto());
    }

    [Fact]
    public void DetalleRecepcion_ToDto_Null_LanzaArgumentNullException()
    {
        DetalleRecepcionEntity? detalle = null;
        Assert.Throws<ArgumentNullException>(() => detalle!.ToDto());
    }
}
