using System.Text.Json;
using ObraSync.Contracts.Events;
using Xunit;

namespace ObraSync.Recepcion.Tests.Events;

public class RecepcionConfirmadaSerializacionTests
{
    [Fact]
    public void Item_serializado_con_opciones_por_defecto_usa_numero_para_cantidades()
    {
        var item = new RecepcionConfirmadaItemEvent(
            ArticuloId: Guid.NewGuid(),
            CantidadRecibida: 10m,
            UnidadMedida: "UN",
            DescripcionArticulo: "Articulo de prueba",
            CantidadEsperada: 10.0000m,
            Conforme: true,
            Observacion: null);

        var json = JsonSerializer.Serialize(item, new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
});
Console.WriteLine(json);

using var doc = JsonDocument.Parse(json);
var root = doc.RootElement;

Assert.Equal(JsonValueKind.Number, root.GetProperty("cantidadRecibida").ValueKind);
Assert.Equal(JsonValueKind.Number, root.GetProperty("cantidadEsperada").ValueKind);
    }
}