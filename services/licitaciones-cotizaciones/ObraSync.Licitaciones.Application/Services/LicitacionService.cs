using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Exceptions;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.Services;

public class LicitacionService : ILicitacionService
{
    private readonly ILicitacionRepository _repo;
    private readonly TimeProvider _time;

    public LicitacionService(ILicitacionRepository repo, TimeProvider time)
    {
        _repo = repo;
        _time = time;
    }

    public async Task<LicitacionResponseDto> CrearAsync(CrearLicitacionRequestDto request, CancellationToken ct = default)
    {
        if (request.RequisicionId == Guid.Empty)
            throw new ValidacionException("requisicionId es obligatorio.");

        var ahora = _time.GetUtcNow().UtcDateTime;
        var limite = ComoUtc(request.FechaLimiteCotizacion);
        if (limite <= ahora)
            throw new ValidacionException("fechaLimiteCotizacion debe ser posterior al momento de publicación (UTC).");

        // Acá solo se guardan requisiciones ya aprobadas: "no existe" y "no está aprobada" son el mismo caso.
        var requisicion = await _repo.ObtenerRequisicionAsync(request.RequisicionId, ct)
            ?? throw new NoEncontradoException("La requisición no existe o no está aprobada.");

        if (await _repo.ExisteAbiertaAsync(request.RequisicionId, ct))
            throw new ConflictoException("Ya existe una licitación abierta para esta requisición.");

        var licitacionId = Guid.NewGuid();
        var licitacion = new Licitacion
        {
            LicitacionId = licitacionId,
            RequisicionId = requisicion.RequisicionId,
            NumeroRequisicion = requisicion.NumeroRequisicion,
            ObraId = requisicion.ObraId,
            Estado = EstadoLicitacion.Abierta,
            FechaCreacion = ahora,
            FechaLimiteCotizacion = limite,
            Items = requisicion.Items.Select(i => new LicitacionItem
            {
                Id = Guid.NewGuid(),
                LicitacionId = licitacionId,
                ArticuloId = i.ArticuloId,
                Descripcion = i.Descripcion,
                Cantidad = i.Cantidad,
                UnidadMedida = i.UnidadMedida
            }).ToList()
        };

        await _repo.AgregarAsync(licitacion, ct);
        return Mapear(licitacion);
    }

    // Una fecha sin zona se interpreta como UTC, que es lo que dice el contrato.
    private static DateTime ComoUtc(DateTime f) => f.Kind switch
    {
        DateTimeKind.Utc => f,
        DateTimeKind.Local => f.ToUniversalTime(),
        _ => DateTime.SpecifyKind(f, DateTimeKind.Utc)
    };

    private static LicitacionResponseDto Mapear(Licitacion l) => new()
    {
        LicitacionId = l.LicitacionId,
        RequisicionId = l.RequisicionId,
        NumeroRequisicion = l.NumeroRequisicion,
        ObraId = l.ObraId,
        Estado = l.Estado.ToString(),
        FechaCreacion = l.FechaCreacion,
        FechaLimiteCotizacion = l.FechaLimiteCotizacion,
        Items = l.Items.Select(i => new PliegoItemDto
        {
            ArticuloId = i.ArticuloId,
            Descripcion = i.Descripcion,
            Cantidad = i.Cantidad,
            UnidadMedida = i.UnidadMedida
        }).ToList()
    };
}