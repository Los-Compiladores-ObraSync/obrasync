namespace ObraSync.Asistente.Application;

/// <summary>Una obra que el usuario puede ver, con los nombres de sus etapas.</summary>
public sealed record ObraAsistente(Guid Id, string Nombre, IReadOnlyList<string> Etapas);

/// <summary>Resultado de listar obras: o hay obras, o hay un código de error estable.</summary>
public sealed record ResultadoObras(IReadOnlyList<ObraAsistente>? Obras, string? Error)
{
    public static ResultadoObras Bien(IReadOnlyList<ObraAsistente> obras) => new(obras, null);
    public static ResultadoObras Falla(string codigo) => new(null, codigo);
}

/// <summary>
/// De dónde salen las obras. La API ya filtra por rol (un Capataz solo ve las suyas),
/// así el asistente nunca arma un borrador para una obra que el usuario no puede ver.
/// </summary>
public interface IBuscadorObras
{
    Task<ResultadoObras> ListarAsync(CancellationToken ct);
}
