namespace ObraSync.Asistente.Application;

/// <summary>Un insumo del catálogo, con solo lo que el modelo necesita ver.</summary>
public sealed record Insumo(string ArticuloId, string Codigo, string Descripcion, string UnidadMedida);

/// <summary>Resultado de una búsqueda: o hay insumos, o hay un código de error estable.</summary>
public sealed record ResultadoBusqueda(IReadOnlyList<Insumo>? Insumos, string? Error)
{
    public static ResultadoBusqueda Bien(IReadOnlyList<Insumo> insumos) => new(insumos, null);
    public static ResultadoBusqueda Falla(string codigo) => new(null, codigo);
}

/// <summary>De dónde salen los insumos. Con una interfaz, la herramienta no sabe si son reales o falsos.</summary>
public interface IBuscadorInsumos
{
    Task<ResultadoBusqueda> BuscarAsync(string texto, CancellationToken ct);
}
