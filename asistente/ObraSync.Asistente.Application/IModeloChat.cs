using System.Text.Json.Nodes;

namespace ObraSync.Asistente.Application;

/// <summary>La respuesta del modelo: su mensaje, por qué terminó y cuántos tokens gastó.</summary>
public sealed record RespuestaModelo(JsonObject Mensaje, string FinReason, int TokensTotales);

/// <summary>Error del modelo con un código estable (nunca el texto crudo del proveedor).</summary>
public sealed class ModeloException : Exception
{
    public string Codigo { get; }
    public ModeloException(string codigo) : base(codigo) => Codigo = codigo;
}

/// <summary>
/// Lo único que el agente necesita saber del modelo. Al ser una interfaz,
/// en las pruebas se reemplaza por un modelo falso y no se gasta cupo de Azure (la CI nunca llama a Azure).
/// </summary>
public interface IModeloChat
{
    Task<RespuestaModelo> PedirAsync(
        JsonArray mensajes, JsonArray? herramientas, int maxTokensSalida, CancellationToken ct);
}
