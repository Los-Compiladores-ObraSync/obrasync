using System;
using ObraSync.Requisiciones.Domain.Constants;

namespace ObraSync.Requisiciones.Domain.Enums;

/// <summary>
/// Unidades de medida válidas para los insumos del catálogo y los ítems de requisición.
/// </summary>
public enum UnidadMedida
{
    Bolsa = 1,
    Kilogramo = 2,
    MetroCubico = 3,
    Unidad = 4
}

public static class UnidadMedidaExtensions
{
    public static string ToCodigo(this UnidadMedida unidad) => unidad switch
    {
        UnidadMedida.Bolsa => UnidadesMedida.Bolsa,
        UnidadMedida.Kilogramo => UnidadesMedida.Kilogramo,
        UnidadMedida.MetroCubico => UnidadesMedida.MetroCubico,
        UnidadMedida.Unidad => UnidadesMedida.Unidad,
        _ => throw new ArgumentOutOfRangeException(nameof(unidad), unidad, "Unidad de medida desconocida.")
    };

    public static bool TryParse(string? valor, out UnidadMedida unidad)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            unidad = default;
            return false;
        }

        switch (valor.Trim().ToUpperInvariant())
        {
            case UnidadesMedida.Bolsa:
                unidad = UnidadMedida.Bolsa;
                return true;
            case UnidadesMedida.Kilogramo:
            case "KILOGRAMO":
                unidad = UnidadMedida.Kilogramo;
                return true;
            case UnidadesMedida.MetroCubico:
            case "METROCUBICO":
                unidad = UnidadMedida.MetroCubico;
                return true;
            case UnidadesMedida.Unidad:
                unidad = UnidadMedida.Unidad;
                return true;
            default:
                unidad = default;
                return false;
        }
    }

    public static UnidadMedida Parse(string? valor)
    {
        if (!TryParse(valor, out var unidad))
        {
            throw new ArgumentException($"La unidad de medida '{valor}' no es válida.", nameof(valor));
        }

        return unidad;
    }
}
