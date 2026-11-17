namespace ObraSync.Licitaciones.Application.Services;

/// <summary>
/// El nombre del proveedor (#206) viene de afuera (el claim "name" del JWT o lo que escribió el Comprador al generar un
/// Magic Link). Antes de guardarlo se limpia: sin caracteres de control, sin espacios de más y con un máximo de 200
/// (el largo de la columna). Si queda vacío se guarda null: no se inventa un nombre.
/// </summary>
public static class NombreProveedor
{
    public const int MaxLargo = 200;

    public static string? Limpiar(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
            return null;
        var limpio = new string(nombre.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        if (limpio.Length == 0)
            return null;
        return limpio.Length <= MaxLargo ? limpio : limpio[..MaxLargo].TrimEnd();
    }
}
