using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ObraSync.Recepcion.Infrastructure.Persistence;

namespace ObraSync.Recepcion.Tests;

/// <summary>
/// Simula lo que hace el consumidor de OrdenCompraDespachadaEvent cuando el proveedor vuelve a despachar
/// una orden cuya última entrega se marcó con un problema (reabre la orden al capataz).
/// </summary>
internal static class RedespachoPruebas
{
    public static async Task RedespacharAsync(RecepcionDbContext db, Guid ordenCompraId)
    {
        var orden = await db.OrdenesCompra.SingleAsync(o => o.Id == ordenCompraId);
        orden.MarcarComoDespachada(DateTime.UtcNow.AddMinutes(1), esRedespacho: true);
        await db.SaveChangesAsync();
    }

    public static async Task RedespacharAsync(IServiceProvider servicios, Guid ordenCompraId)
    {
        using var scope = servicios.CreateScope();
        await RedespacharAsync(scope.ServiceProvider.GetRequiredService<RecepcionDbContext>(), ordenCompraId);
    }
}
