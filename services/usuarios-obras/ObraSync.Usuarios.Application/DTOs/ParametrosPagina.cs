namespace ObraSync.Usuarios.Application.DTOs;

public sealed record ParametrosPagina(int Pagina = 1, int Tamano = 50)
{
    public const int TamanoMaximo = 50;

    public string? Validar()
    {
        if (Pagina < 1) return "La página debe ser 1 o mayor.";
        if (Tamano < 1 || Tamano > TamanoMaximo) return $"El tamaño debe estar entre 1 y {TamanoMaximo}.";
        return null;
    }

    public int Saltear => (Pagina - 1) * Tamano;
}
