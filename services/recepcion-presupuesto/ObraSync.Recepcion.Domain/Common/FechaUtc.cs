using System;

namespace ObraSync.Recepcion.Domain.Common;

/// <summary>
/// Normaliza fechas a UTC preservando instantes locales y tratando fechas sin Kind como UTC.
/// </summary>
public static class FechaUtc
{
    public static DateTime Normalizar(DateTime valor) => valor.Kind switch
    {
        DateTimeKind.Unspecified => DateTime.SpecifyKind(valor, DateTimeKind.Utc),
        DateTimeKind.Local => valor.ToUniversalTime(),
        _ => valor
    };

    public static DateTime? Normalizar(DateTime? valor) =>
        valor.HasValue ? Normalizar(valor.Value) : null;
}
