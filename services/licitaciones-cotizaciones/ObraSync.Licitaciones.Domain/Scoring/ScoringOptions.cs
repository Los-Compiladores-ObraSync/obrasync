namespace ObraSync.Licitaciones.Domain.Scoring;

public enum ModoNormalizacionScoring
{
    RelativoMejorOferta = 0,
    MinMax = 1
}

public sealed class ScoringOptions
{
    public const string SectionName = "Scoring";

    public decimal PesoPrecio { get; set; } = 0.50m;
    public decimal PesoPlazo { get; set; } = 0.25m;
    public decimal PesoFlete { get; set; } = 0.25m;

    /// <summary>
    /// Mientras no exista fuente de calificación de proveedores (#88/#197), su peso por defecto es 0
    /// para no agregar peso muerto al puntaje total.
    /// </summary>
    public decimal PesoCalificacion { get; set; } = 0.00m;

    public ModoNormalizacionScoring ModoNormalizacion { get; set; } = ModoNormalizacionScoring.RelativoMejorOferta;

    public void Validar()
    {
        if (PesoPrecio < 0)
            throw new InvalidOperationException("El peso de Precio en Scoring ('Scoring:PesoPrecio') no puede ser negativo.");
        if (PesoPlazo < 0)
            throw new InvalidOperationException("El peso de Plazo en Scoring ('Scoring:PesoPlazo') no puede ser negativo.");
        if (PesoFlete < 0)
            throw new InvalidOperationException("El peso de Flete en Scoring ('Scoring:PesoFlete') no puede ser negativo.");
        if (PesoCalificacion < 0)
            throw new InvalidOperationException("El peso de Calificación en Scoring ('Scoring:PesoCalificacion') no puede ser negativo.");

        if (PesoPrecio + PesoPlazo + PesoFlete + PesoCalificacion <= 0)
            throw new InvalidOperationException("La suma de los pesos de Scoring ('Scoring:PesoPrecio + PesoPlazo + PesoFlete + PesoCalificacion') debe ser estrictamente mayor a cero.");
    }

    public PonderacionScoring ToPonderacion()
    {
        Validar();
        return new PonderacionScoring(
            precio: PesoPrecio,
            flete: PesoFlete,
            plazo: PesoPlazo,
            calificacion: PesoCalificacion);
    }
}
