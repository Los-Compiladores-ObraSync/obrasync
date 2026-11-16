using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ObraSync.Recepcion.Domain.Presupuestos;

/// <summary>
/// Normalizador de texto para etapas de obras y partidas presupuestarias (#196).
/// Resuelve discrepancias de tipeo libre:
/// 1. Elimina espacios en blanco iniciales, finales y colapsa espacios múltiples.
/// 2. Ignora mayúsculas y minúsculas (convierte a minúsculas invariantes).
/// 3. Remueve diacríticos y tildes (e.g. 'á' -> 'a', 'é' -> 'e', 'í' -> 'i', 'ó' -> 'o', 'ú' -> 'u', 'ñ' -> 'n').
/// </summary>
public static class NormalizadorEtapa
{
    private static readonly Regex EspaciosMultiplesRegex = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Normaliza una cadena de texto de etapa. Devuelve null si la etapa es nula, vacía o solo espacios.
    /// </summary>
    public static string? Normalizar(string? etapa)
    {
        if (string.IsNullOrWhiteSpace(etapa))
            return null;

        // 1. Recortar extremos y colapsar espacios múltiples internos
        var limpio = EspaciosMultiplesRegex.Replace(etapa.Trim(), " ");

        // 2. A minúsculas invariantes
        var minusculas = limpio.ToLowerInvariant();

        // 3. Descomponer diacríticos (FormD) y remover marcas no espaciadas
        var descompuesto = minusculas.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(descompuesto.Length);

        foreach (var c in descompuesto)
        {
            var categoria = CharUnicodeInfo.GetUnicodeCategory(c);
            if (categoria != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Determina si dos nombres de etapa son equivalentes tras aplicar la normalización.
    /// Dos etapas nulas o vacías se consideran equivalentes (ambas representan 'sin etapa' o 'toda la obra').
    /// </summary>
    public static bool SonEquivalentes(string? etapaA, string? etapaB)
    {
        var normA = Normalizar(etapaA);
        var normB = Normalizar(etapaB);

        if (normA is null && normB is null)
            return true;

        if (normA is null || normB is null)
            return false;

        return string.Equals(normA, normB, StringComparison.Ordinal);
    }
}
