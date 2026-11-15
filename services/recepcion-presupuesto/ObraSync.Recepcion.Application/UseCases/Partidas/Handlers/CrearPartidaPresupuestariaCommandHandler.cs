using System;
using Microsoft.EntityFrameworkCore;
using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Exceptions;
using ObraSync.Recepcion.Application.Mappings;
using ObraSync.Recepcion.Application.UseCases.Partidas.Commands;
using ObraSync.Recepcion.Domain.Entities;

namespace ObraSync.Recepcion.Application.UseCases.Partidas.Handlers;

public class CrearPartidaPresupuestariaCommandHandler
{
    private readonly IRecepcionDbContext _context;

    public CrearPartidaPresupuestariaCommandHandler(IRecepcionDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<PartidaPresupuestariaDto> Handle(
        CrearPartidaPresupuestariaCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command == null || command.Request == null)
            throw new ArgumentNullException(nameof(command), "El comando de creación no puede ser nulo.");

        var req = command.Request;

        if (req.ObraId == Guid.Empty)
            throw new ArgumentException("El identificador de la obra es obligatorio.", nameof(req.ObraId));

        if (string.IsNullOrWhiteSpace(req.CodigoPartida))
            throw new ArgumentException("El código de partida es obligatorio.", nameof(req.CodigoPartida));

        if (string.IsNullOrWhiteSpace(req.Descripcion))
            throw new ArgumentException("La descripción de la partida es obligatoria.", nameof(req.Descripcion));

        if (req.MontoAsignado < 0)
            throw new ArgumentException("El monto asignado no puede ser negativo.", nameof(req.MontoAsignado));

        var codigoLimpio = req.CodigoPartida.Trim();
        var etapaLimpia = string.IsNullOrWhiteSpace(req.Etapa) ? null : req.Etapa.Trim();

        // Control de duplicados por código de partida y obra (#93)
        var codigoDuplicado = await _context.PartidasPresupuestarias
            .AnyAsync(p => p.ObraId == req.ObraId && p.CodigoPartida == codigoLimpio, cancellationToken);

        if (codigoDuplicado)
        {
            throw new RecepcionConflictoException($"Ya existe una partida con el código '{codigoLimpio}' para esta obra.");
        }

        var partida = new PartidaPresupuestaria
        {
            Id = Guid.NewGuid(),
            ObraId = req.ObraId,
            Etapa = etapaLimpia,
            CodigoPartida = codigoLimpio,
            Descripcion = req.Descripcion.Trim(),
            MontoAsignado = req.MontoAsignado,
            MontoComprometido = 0m,
            MontoEjecutado = 0m,
            Moneda = string.IsNullOrWhiteSpace(req.Moneda) ? "ARS" : req.Moneda.Trim().ToUpperInvariant(),
            CreatedAt = DateTime.UtcNow
        };

        await _context.PartidasPresupuestarias.AddAsync(partida, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return partida.ToDto();
    }
}
