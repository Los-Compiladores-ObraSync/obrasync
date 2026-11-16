namespace ObraSync.Asistente.Application;

/// <summary>Lo que el modelo leyó de la foto de un remito. Cada campo es null si falta, es ilegible o es ambiguo.</summary>
public sealed record LecturaCruda(string? NumeroRemito, string? Fecha, int Tokens);

/// <summary>Lo que se le devuelve al cliente: ya limpio y validado. Cada campo es null si no se pudo leer.</summary>
public sealed record LecturaRemito(string? NumeroRemito, DateOnly? Fecha);

/// <summary>Resultado de leer un remito: la lectura (con tokens gastados) o un código de error estable.</summary>
public sealed record ResultadoLecturaRemito(LecturaRemito? Lectura, int Tokens, string? Error)
{
    public static ResultadoLecturaRemito Bien(LecturaRemito lectura, int tokens) => new(lectura, tokens, null);
    public static ResultadoLecturaRemito Falla(string codigo) => new(null, 0, codigo);
}

/// <summary>
/// Lee el número y la fecha de la foto de un remito con el modelo de IA (#209).
/// Al ser una interfaz, en las pruebas se usa uno falso y no se gasta cupo (la CI nunca llama a Azure).
/// Lanza <see cref="ModeloException"/> con un código estable si el modelo falla.
/// </summary>
public interface IExtractorRemito
{
    Task<LecturaCruda> ExtraerAsync(byte[] imagen, string tipoMedia, CancellationToken ct);
}
