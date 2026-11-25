namespace ObraSync.Asistente.Application;

/// <summary>
/// Puerto de consulta de solo lectura del presupuesto y órdenes bloqueadas de una obra (ISP y DIP).
/// Compartido entre las herramientas de Compras del chat y el servidor MCP de presupuestos.
/// </summary>
public interface IConsultaPresupuestoObra
{
    Task<ResultadoHerramienta> ConsultarAsync(string obraIdTexto, string? etapa, CancellationToken ct);
}
