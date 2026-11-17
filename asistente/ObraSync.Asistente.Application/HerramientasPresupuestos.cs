using System.Text.Json;
using System.Text.Json.Nodes;

namespace ObraSync.Asistente.Application;

/// <summary>Consulta de solo lectura de las partidas presupuestarias de una obra.</summary>
public sealed class HerramientasPresupuestos
{
    public const string ConsultarPartidas = "consultar_partidas_presupuestarias";

    private static readonly JsonSerializerOptions OpcionesJsonWeb = new(JsonSerializerDefaults.Web);
    private readonly IConsultorPresupuestos _presupuestos;

    public HerramientasPresupuestos(IConsultorPresupuestos presupuestos) => _presupuestos = presupuestos;

    public async Task<ResultadoHerramienta> ConsultarAsync(
        string obraIdTexto,
        string? etapa,
        CancellationToken ct)
    {
        if (!Guid.TryParse(obraIdTexto, out var obraId) || obraId == Guid.Empty)
            return ResultadoHerramienta.Error("argumentos_invalidos");

        var resultado = await _presupuestos.ListarPartidasAsync(
            obraId,
            string.IsNullOrWhiteSpace(etapa) ? null : etapa,
            ct);
        if (resultado.Error is not null)
            return ResultadoHerramienta.Error(resultado.Error);

        var partidas = JsonSerializer.SerializeToNode(resultado.Partidas, OpcionesJsonWeb)?.AsArray() ?? new JsonArray();
        var total = resultado.Total ?? partidas.Count;
        return ResultadoHerramienta.Bien(new JsonObject
        {
            ["ok"] = true,
            ["total"] = total,
            ["mostradas"] = partidas.Count,
            ["hayMas"] = total > partidas.Count,
            ["partidas"] = partidas
        });
    }
}
