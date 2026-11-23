using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObraSync.Requisiciones.Application.DTOs;
using ObraSync.Requisiciones.Application.Interfaces;
using ObraSync.Requisiciones.Domain.Entities;

namespace ObraSync.Requisiciones.Application.UseCases.Requisiciones.Handlers;

internal static class CatalogoItemsResolver
{
    public static IReadOnlyList<Guid> ExtraerArticuloIdsValidos(IReadOnlyList<CrearRequisicionItemDto>? items)
    {
        if (items is null || items.Count == 0)
            throw new ArgumentException("La requisición debe contener al menos un ítem.", nameof(items));

        var articuloIds = items.Select(i => i.ArticuloId).Distinct().ToList();
        if (articuloIds.Any(id => id == Guid.Empty))
            throw new ArgumentException("Todos los ítems deben tener un articuloId válido.", nameof(items));

        return articuloIds;
    }

    public static async Task<List<RequisicionItem>> ConstruirItemsAsync(
        this IInsumoRepository insumoRepository,
        Guid requisicionId,
        IReadOnlyList<CrearRequisicionItemDto> items,
        CancellationToken cancellationToken)
    {
        var articuloIds = ExtraerArticuloIdsValidos(items);
        var insumos = await insumoRepository.GetByIdsAsync(articuloIds, cancellationToken);
        var insumosPorId = insumos.ToDictionary(i => i.Id);

        var resultado = new List<RequisicionItem>(items.Count);
        foreach (var item in items)
        {
            if (!insumosPorId.TryGetValue(item.ArticuloId, out var insumo))
            {
                throw new KeyNotFoundException($"No se encontró el artículo con Id '{item.ArticuloId}' en el catálogo.");
            }

            resultado.Add(RequisicionItem.CrearDesde(requisicionId, insumo, item.Cantidad));
        }

        return resultado;
    }
}
