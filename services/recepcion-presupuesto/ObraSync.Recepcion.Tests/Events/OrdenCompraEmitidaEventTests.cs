using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ObraSync.Contracts.Events;
using Xunit;
using Xunit.Abstractions;

namespace ObraSync.Recepcion.Tests.Events;

public class OrdenCompraEmitidaEventTests
{
    private readonly ITestOutputHelper _output;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public OrdenCompraEmitidaEventTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Verificar_TiposExportados_SystemTextJson_En_MassTransit_Dll()
    {
        // Inspección real del ensamblado MassTransit 8.5.12
        var massTransitAssembly = typeof(MassTransit.IBus).Assembly;
        var exportedTypes = massTransitAssembly.GetExportedTypes()
            .Where(t => t.Name.Contains("SystemTextJson", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.FullName ?? t.Name)
            .ToList();

        // Se confirma que SystemTextJsonConstant NO existe en MassTransit 8.5.12
        Assert.DoesNotContain(exportedTypes, name => name.Contains("SystemTextJsonConstant", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Deserializar_JsonValidoDelContrato_DebeMapearTodasLasPropiedadesCorrectamente()
    {
        // JSON de ejemplo válido conforme a contracts/events/OrdenCompraEmitidaEvent.json
        var json = """
        {
          "eventId": "11111111-1111-1111-1111-111111111111",
          "occurredAt": "2026-10-27T20:00:00Z",
          "correlationId": "22222222-2222-2222-2222-222222222222",
          "ordenCompraId": "33333333-3333-3333-3333-333333333333",
          "licitacionId": "44444444-4444-4444-4444-444444444444",
          "requisicionId": "55555555-5555-5555-5555-555555555555",
          "obraId": "66666666-6666-6666-6666-666666666666",
          "proveedorId": "77777777-7777-7777-7777-777777777777",
          "montoTotal": 1250000.50,
          "items": [
            {
              "articuloId": "88888888-8888-8888-8888-888888888888",
              "descripcion": "Cemento Portland Normal",
              "cantidad": 150.5,
              "unidadMedida": "BOLSA",
              "precioUnitario": 8305.65
            }
          ]
        }
        """;

        var evento = JsonSerializer.Deserialize<OrdenCompraEmitidaEvent>(json, _jsonOptions);

        Assert.NotNull(evento);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), evento.EventId);
        Assert.Equal(DateTime.Parse("2026-10-27T20:00:00Z").ToUniversalTime(), evento.OccurredAt.ToUniversalTime());
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), evento.CorrelationId);
        Assert.Equal(Guid.Parse("33333333-3333-3333-3333-333333333333"), evento.OrdenCompraId);
        Assert.Equal(Guid.Parse("44444444-4444-4444-4444-444444444444"), evento.LicitacionId);
        Assert.Equal(Guid.Parse("55555555-5555-5555-5555-555555555555"), evento.RequisicionId);
        Assert.Equal(Guid.Parse("66666666-6666-6666-6666-666666666666"), evento.ObraId);
        Assert.Equal(Guid.Parse("77777777-7777-7777-7777-777777777777"), evento.ProveedorId);
        Assert.Equal(1250000.50m, evento.MontoTotal);

        Assert.Single(evento.Items);
        var item = evento.Items[0];
        Assert.Equal(Guid.Parse("88888888-8888-8888-8888-888888888888"), item.ArticuloId);
        Assert.Equal("Cemento Portland Normal", item.Descripcion);
        Assert.Equal(150.5m, item.Cantidad);
        Assert.Equal("BOLSA", item.UnidadMedida);
        Assert.Equal(8305.65m, item.PrecioUnitario);
    }

    [Fact]
    public void Deserializar_ConPropiedadExtraEnRaiz_DebeLanzarJsonException()
    {
        // Contrato exige additionalProperties: false en raíz
        var jsonConExtra = """
        {
          "eventId": "11111111-1111-1111-1111-111111111111",
          "occurredAt": "2026-10-27T20:00:00Z",
          "correlationId": "22222222-2222-2222-2222-222222222222",
          "ordenCompraId": "33333333-3333-3333-3333-333333333333",
          "licitacionId": "44444444-4444-4444-4444-444444444444",
          "requisicionId": "55555555-5555-5555-5555-555555555555",
          "obraId": "66666666-6666-6666-6666-666666666666",
          "proveedorId": "77777777-7777-7777-7777-777777777777",
          "montoTotal": 1250000.50,
          "items": [
            {
              "articuloId": "88888888-8888-8888-8888-888888888888",
              "cantidad": 10,
              "unidadMedida": "UNIDAD",
              "precioUnitario": 100
            }
          ],
          "propiedadNoPermitida": "invalido"
        }
        """;

        // Vía 1: Con opciones explícitas que exigen UnmappedMemberHandling.Disallow
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<OrdenCompraEmitidaEvent>(jsonConExtra, _jsonOptions));

        // Vía 2: Opciones por defecto (demuestra que el atributo de clase [JsonUnmappedMemberHandling(Disallow)] hace efecto)
        var optionsDefault = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<OrdenCompraEmitidaEvent>(jsonConExtra, optionsDefault));
    }

    [Fact]
    public void Deserializar_ConPropiedadExtraEnItem_DebeLanzarJsonException()
    {
        // Contrato exige additionalProperties: false en el schema de items
        var jsonConExtraEnItem = """
        {
          "eventId": "11111111-1111-1111-1111-111111111111",
          "occurredAt": "2026-10-27T20:00:00Z",
          "correlationId": "22222222-2222-2222-2222-222222222222",
          "ordenCompraId": "33333333-3333-3333-3333-333333333333",
          "licitacionId": "44444444-4444-4444-4444-444444444444",
          "requisicionId": "55555555-5555-5555-5555-555555555555",
          "obraId": "66666666-6666-6666-6666-666666666666",
          "proveedorId": "77777777-7777-7777-7777-777777777777",
          "montoTotal": 1250000.50,
          "items": [
            {
              "articuloId": "88888888-8888-8888-8888-888888888888",
              "cantidad": 10,
              "unidadMedida": "UNIDAD",
              "precioUnitario": 100,
              "color": "rojo"
            }
          ]
        }
        """;

        var optionsDefault = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<OrdenCompraEmitidaEvent>(jsonConExtraEnItem, optionsDefault));
    }
}
