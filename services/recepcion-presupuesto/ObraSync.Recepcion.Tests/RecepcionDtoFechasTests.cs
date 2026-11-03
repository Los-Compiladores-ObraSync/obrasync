using ObraSync.Recepcion.Api.DTOs;
using RecepcionEntity = ObraSync.Recepcion.Domain.Entities.Recepcion;

namespace ObraSync.Recepcion.Tests;

public class RecepcionDtoFechasTests
{
    [Fact]
    public void RecepcionDto_NormalizaFechasAUtc()
    {
        var fechaOriginal = new DateTime(2026, 10, 29, 12, 30, 45, DateTimeKind.Unspecified);
        var entidad = new RecepcionEntity
        {
            Id = Guid.NewGuid(),
            ObraId = Guid.NewGuid(),
            OrdenCompraId = Guid.NewGuid(),
            NumeroRemito = "REM-FECHA-1",
            FechaRecepcion = fechaOriginal,
            CreatedAt = fechaOriginal,
            UpdatedAt = fechaOriginal
        };

        var dto = RecepcionDto.DesdeEntidad(entidad);

        Assert.Equal(DateTimeKind.Utc, dto.FechaRecepcion.Kind);
        Assert.Equal(fechaOriginal.Ticks, dto.FechaRecepcion.Ticks);
        Assert.Equal(fechaOriginal.Hour, dto.FechaRecepcion.Hour);
        Assert.Equal(DateTimeKind.Utc, dto.CreatedAt.Kind);
        Assert.NotNull(dto.UpdatedAt);
        Assert.Equal(DateTimeKind.Utc, dto.UpdatedAt!.Value.Kind);
    }
}
