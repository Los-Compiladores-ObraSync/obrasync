using ObraSync.Requisiciones.Domain.Constants;

namespace ObraSync.Requisiciones.Application.UseCases.Catalog.Commands
{
    public record CreateInsumoCommand(
        string Codigo,
        string Descripcion,
        string UnidadMedida,
        bool Activo = true,
        decimal? PrecioReferencia = null,
        string Moneda = Monedas.PesoArgentino
    );
}
