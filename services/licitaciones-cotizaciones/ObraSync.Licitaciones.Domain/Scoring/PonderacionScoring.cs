namespace ObraSync.Licitaciones.Domain.Scoring;

/// <summary>Pesos relativos de cada criterio. Son parámetros: no hay valores fijos en el código.</summary>
public sealed class PonderacionScoring
{
    public decimal Precio { get; }
    public decimal Flete { get; }
    public decimal Plazo { get; }
    public decimal Calificacion { get; }

    public PonderacionScoring(decimal precio, decimal flete, decimal plazo, decimal calificacion = 0)
    {
        if (precio < 0) throw new ArgumentOutOfRangeException(nameof(precio), "El peso no puede ser negativo.");
        if (flete < 0) throw new ArgumentOutOfRangeException(nameof(flete), "El peso no puede ser negativo.");
        if (plazo < 0) throw new ArgumentOutOfRangeException(nameof(plazo), "El peso no puede ser negativo.");
        if (calificacion < 0) throw new ArgumentOutOfRangeException(nameof(calificacion), "El peso no puede ser negativo.");
        if (precio + flete + plazo + calificacion <= 0)
            throw new ArgumentException("La suma de los pesos debe ser mayor a cero.");

        Precio = precio;
        Flete = flete;
        Plazo = plazo;
        Calificacion = calificacion;
    }

    public decimal Total => Precio + Flete + Plazo + Calificacion;
}