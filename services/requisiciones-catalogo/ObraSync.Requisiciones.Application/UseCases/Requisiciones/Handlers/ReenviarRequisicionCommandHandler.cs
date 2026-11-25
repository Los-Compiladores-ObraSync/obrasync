using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ObraSync.Requisiciones.Application.DTOs;
using ObraSync.Requisiciones.Application.Interfaces;
using ObraSync.Requisiciones.Application.Mappings;
using ObraSync.Requisiciones.Application.UseCases.Requisiciones.Commands;
using ObraSync.Requisiciones.Domain.Entities;

namespace ObraSync.Requisiciones.Application.UseCases.Requisiciones.Handlers;

public class ReenviarRequisicionCommandHandler : ICommandHandler<ReenviarRequisicionCommand, RequisicionResponseDto>
{
    private readonly IRequisicionRepository _requisicionRepository;
    private readonly IInsumoRepository _insumoRepository;

    public ReenviarRequisicionCommandHandler(
        IRequisicionRepository requisicionRepository,
        IInsumoRepository insumoRepository)
    {
        _requisicionRepository = requisicionRepository ?? throw new ArgumentNullException(nameof(requisicionRepository));
        _insumoRepository = insumoRepository ?? throw new ArgumentNullException(nameof(insumoRepository));
    }

    public async Task<RequisicionResponseDto> Handle(
        ReenviarRequisicionCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidarEntrada(command);

        var requisicion = await _requisicionRepository.GetByIdAsync(command.RequisicionId, cancellationToken)
            ?? throw new KeyNotFoundException($"No se encontró la requisición con Id '{command.RequisicionId}'.");

        requisicion.AsegurarPuedeSerModificadaPor(command.UsuarioId, command.EsAdmin, "editarla y reenviarla");
        requisicion.AsegurarPuedeEditarse();

        var nuevosItems = await _insumoRepository.ConstruirItemsAsync(requisicion.Id, command.Items, cancellationToken);
        requisicion.Reenviar(nuevosItems, command.Etapa, command.Observaciones);

        await _requisicionRepository.SaveChangesAsync(cancellationToken);
        return requisicion.ToDto();
    }

    private static void ValidarEntrada(ReenviarRequisicionCommand command)
    {
        if (command.RequisicionId == Guid.Empty)
            throw new ArgumentException("El identificador de la requisición es obligatorio.", nameof(command));

        if (command.UsuarioId == Guid.Empty)
            throw new ArgumentException("El identificador del usuario es obligatorio.", nameof(command));

        CatalogoItemsResolver.ExtraerArticuloIdsValidos(command.Items);
        Requisicion.NormalizarObservaciones(command.Observaciones);
    }
}
