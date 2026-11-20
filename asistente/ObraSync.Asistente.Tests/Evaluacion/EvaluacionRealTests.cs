using System.Globalization;
using System.Text;
using ObraSync.Asistente.Infrastructure;
using Xunit.Abstractions;

namespace ObraSync.Asistente.Tests.Evaluacion;

/// <summary>
/// Solo corre si se pide con EVAL_REAL=1 y hay clave. Sin eso se saltea, así que la CI nunca llama a Azure.
/// </summary>
public sealed class FactModeloRealAttribute : FactAttribute
{
    public FactModeloRealAttribute()
    {
        if (Environment.GetEnvironmentVariable("EVAL_REAL") != "1")
            Skip = "Evaluación contra el modelo real: se pide con EVAL_REAL=1 (ver asistente/evaluacion/README.md).";
        else if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AI_API_KEY"))
                 || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AI_BASE_URL")))
            Skip = "Faltan AI_API_KEY y AI_BASE_URL (variables de entorno; nunca se escriben en el repo).";
    }
}

/// <summary>
/// Evaluación del agente por rol (#145), modo manual: el MISMO guion de casos, pero contra el modelo real.
/// Sirve para ver cómo se comporta el modelo de verdad. Gasta cupo compartido del grupo (#144): avisar en el canal antes.
/// Variables: EVAL_REAL=1, AI_API_KEY, AI_BASE_URL; opcionales AI_DEPLOYMENT, EVAL_ROL (ej. "Capataz,Comprador"),
/// EVAL_PAUSA_SEG (pausa entre casos, 15 por defecto) y EVAL_SALIDA (carpeta del informe).
/// </summary>
public class EvaluacionRealTests
{
    private readonly ITestOutputHelper _salida;

    public EvaluacionRealTests(ITestOutputHelper salida) => _salida = salida;

    private sealed record Fila(string Nombre, string Estado, string? Motivo, int Tokens, int Herramientas, int Iteraciones, string Texto);

    [FactModeloReal]
    [Trait("Category", "EvaluacionReal")]
    public async Task Evaluacion_contra_el_modelo_real()
    {
        var config = new ConfiguracionIA(
            Environment.GetEnvironmentVariable("AI_BASE_URL"),
            Environment.GetEnvironmentVariable("AI_DEPLOYMENT"),
            Environment.GetEnvironmentVariable("AI_API_VERSION"),
            Environment.GetEnvironmentVariable("AI_API_KEY"),
            null,
            Environment.GetEnvironmentVariable("AI_REASONING_EFFORT"));
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var modelo = new ClienteGateway(http, config);

        var roles = (Environment.GetEnvironmentVariable("EVAL_ROL") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var pausa = int.TryParse(Environment.GetEnvironmentVariable("EVAL_PAUSA_SEG"), out var p) && p >= 0 ? p : 15;
        var escenarios = EscenariosEvaluacion.Todos
            .Where(e => roles.Length == 0 || roles.Contains(e.Rol, StringComparer.OrdinalIgnoreCase))
            .ToList();
        Assert.NotEmpty(escenarios);

        var filas = new List<Fila>();
        for (var n = 0; n < escenarios.Count; n++)
        {
            var escenario = escenarios[n];
            var entorno = new EntornoEvaluacion(escenario.Rol);
            escenario.Preparar(entorno);

            var r = await entorno.CrearAgente(modelo).ResponderAsync(escenario.Pregunta);

            var (estado, motivo) = r.Error switch
            {
                null => escenario.Verificar(r, entorno) is { } fallo ? ("Falló", fallo) : ("Aprobado", null),
                "modelo_limite_de_tasa" => ("No evaluado", "el modelo respondió 429 (límite de tasa compartido): repetir más tarde"),
                var otro => ("Falló", $"el agente devolvió el error '{otro}'")
            };

            var fila = new Fila(escenario.Nombre, estado, motivo, r.Tokens, r.Herramientas, r.Iteraciones, r.Texto);
            filas.Add(fila);
            _salida.WriteLine($"[{estado}] {escenario.Nombre} | tokens {r.Tokens} | herramientas {r.Herramientas}"
                              + (motivo is null ? "" : $" | {motivo}"));
            _salida.WriteLine($"    Respuesta: {r.Texto}");

            if (n < escenarios.Count - 1 && pausa > 0)
                await Task.Delay(TimeSpan.FromSeconds(pausa));
        }

        var ruta = EscribirInforme(config, filas);
        _salida.WriteLine($"Informe: {ruta}");
        _salida.WriteLine($"Aprobados {filas.Count(f => f.Estado == "Aprobado")}, fallos {filas.Count(f => f.Estado == "Falló")}, "
                          + $"no evaluados {filas.Count(f => f.Estado == "No evaluado")}, tokens {filas.Sum(f => f.Tokens)}.");

        Assert.DoesNotContain(filas, f => f.Estado == "Falló");
    }

    private static string EscribirInforme(ConfiguracionIA config, List<Fila> filas)
    {
        var carpeta = Environment.GetEnvironmentVariable("EVAL_SALIDA");
        if (string.IsNullOrWhiteSpace(carpeta))
        {
            // Sube desde bin/Debug/net8.0 hasta la carpeta "asistente" (la que tiene el .slnx).
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ObraSync.Asistente.slnx")))
                dir = dir.Parent;
            carpeta = Path.Combine(dir?.FullName ?? Path.GetTempPath(), "evaluacion", "resultados");
        }
        Directory.CreateDirectory(carpeta);

        var ahora = DateTime.UtcNow;
        var ruta = Path.Combine(carpeta, $"{ahora:yyyy-MM-dd-HHmm}-utc.md");
        var sb = new StringBuilder();
        sb.AppendLine($"# Corrida real del asistente — {ahora.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC");
        sb.AppendLine();
        sb.AppendLine($"Modelo: `{config.Deployment}`. Casos corridos: {filas.Count}. Tokens gastados: {filas.Sum(f => f.Tokens)}.");
        sb.AppendLine();
        sb.AppendLine("| Caso | Resultado | Tokens | Herramientas | Motivo |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var f in filas)
            sb.AppendLine($"| {f.Nombre} | {f.Estado} | {f.Tokens} | {f.Herramientas} | {f.Motivo?.Replace("|", "/")} |");
        sb.AppendLine();
        sb.AppendLine("## Qué respondió el modelo");
        foreach (var f in filas)
        {
            sb.AppendLine();
            sb.AppendLine($"### {f.Nombre} — {f.Estado}");
            foreach (var linea in (string.IsNullOrWhiteSpace(f.Texto) ? "(sin texto)" : f.Texto).Split('\n'))
                sb.AppendLine($"> {linea.TrimEnd()}");
        }
        File.WriteAllText(ruta, sb.ToString(), new UTF8Encoding(false));
        return ruta;
    }
}
