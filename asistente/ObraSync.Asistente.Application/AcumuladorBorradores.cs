namespace ObraSync.Asistente.Application;

/// <summary>
/// Acumula de forma inmutable los borradores generados por las herramientas durante una conversación del <see cref="Agente"/>,
/// aislando al bucle principal de conocer cada variante concreta de borrador (SRP).
/// </summary>
public sealed record AcumuladorBorradores(
    BorradorRequisicion? Borrador = null,
    BorradorAprobacion? Aprobacion = null,
    BorradorRecepcion? Recepcion = null,
    BorradorAdjudicacion? Adjudicacion = null)
{
    public static readonly AcumuladorBorradores Vacio = new();

    public bool TieneAlguno =>
        Borrador is not null ||
        Aprobacion is not null ||
        Recepcion is not null ||
        Adjudicacion is not null;

    public AcumuladorBorradores Acumular(ResultadoHerramienta resultado) => new(
        resultado.Borrador ?? Borrador,
        resultado.Aprobacion ?? Aprobacion,
        resultado.Recepcion ?? Recepcion,
        resultado.Adjudicacion ?? Adjudicacion);

    public RespuestaAgente CrearRespuesta(string texto, int iteraciones, int herramientas, int tokens) =>
        new(true, texto, null, iteraciones, herramientas, tokens, Borrador, Aprobacion, Recepcion, Adjudicacion);
}
