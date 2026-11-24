using System.Reflection;
using System.Text.Json;
using ObraSync.Contracts.Events;
using Xunit;

namespace ObraSync.Recepcion.Tests.Events;

/// <summary>
/// Vigila que los eventos de Recepción tengan exactamente los campos de su JSON Schema en contracts/events.
/// Los eventos de Recepción rechazan campos desconocidos: si otro squad agrega un campo al contrato y acá no se
/// agrega a la copia del evento, el mensaje falla al deserializarse y termina en la cola _error (así pasó con
/// EsRedespacho / NumeroDespacho de la tarjeta #248). Este test hace que ese desfasaje rompa el build.
/// </summary>
public class ContratoEventosTests
{
    public static TheoryData<string, Type> Eventos => new()
    {
        { "OrdenCompraEmitidaEvent", typeof(OrdenCompraEmitidaEvent) },
        { "OrdenCompraDespachadaEvent", typeof(OrdenCompraDespachadaEvent) },
        { "OrdenCompraBloqueadaEvent", typeof(OrdenCompraBloqueadaEvent) },
        { "RecepcionConfirmadaEvent", typeof(RecepcionConfirmadaEvent) },
        { "RecepcionConProblemaEvent", typeof(RecepcionConProblemaEvent) },
    };

    [Theory]
    [MemberData(nameof(Eventos))]
    public void El_evento_tiene_los_mismos_campos_que_su_schema(string nombreSchema, Type tipoEvento)
    {
        var rutaSchema = Path.Combine(RaizDelRepositorio(), "contracts", "events", $"{nombreSchema}.json");
        using var documento = JsonDocument.Parse(File.ReadAllText(rutaSchema));

        var camposDelSchema = documento.RootElement.GetProperty("properties")
            .EnumerateObject()
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var camposDelEvento = tipoEvento
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .First()
            .GetParameters()
            .Select(p => char.ToLowerInvariant(p.Name![0]) + p.Name[1..])
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(camposDelSchema, camposDelEvento);
    }

    private static string RaizDelRepositorio()
    {
        var directorio = new DirectoryInfo(AppContext.BaseDirectory);
        while (directorio is not null && !Directory.Exists(Path.Combine(directorio.FullName, "contracts", "events")))
        {
            directorio = directorio.Parent;
        }

        return directorio?.FullName
            ?? throw new InvalidOperationException(
                "No se encontró la carpeta contracts/events subiendo desde el directorio de las pruebas. " +
                "Las pruebas de contrato se corren dentro del repositorio completo.");
    }
}
