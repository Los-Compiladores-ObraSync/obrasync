using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ObraSync.Recepcion.Infrastructure.Persistence;

namespace ObraSync.Recepcion.Tests;

internal static class RedespachoDePrueba
{
    public static async Task RegistrarAsync(IServiceProvider services, Guid ordenId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();
        await RegistrarAsync(db, ordenId);
    }

    public static async Task RegistrarAsync(RecepcionDbContext db, Guid ordenId)
    {
        var orden = await db.OrdenesCompra.SingleAsync(o => o.Id == ordenId);
        var ultimaRecepcion = await db.Recepciones
            .Where(r => r.OrdenCompraId == ordenId)
            .MaxAsync(r => r.CreatedAt);
        var fechaDespacho = ultimaRecepcion > orden.FechaDespacho!.Value
            ? ultimaRecepcion
            : orden.FechaDespacho.Value;

        orden.MarcarComoDespachada(fechaDespacho.AddTicks(1), esRedespacho: true);
        await db.SaveChangesAsync();
    }
}
