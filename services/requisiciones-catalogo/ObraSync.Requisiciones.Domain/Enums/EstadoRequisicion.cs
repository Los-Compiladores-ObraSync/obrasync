using System;
using ConstantesEstado = ObraSync.Requisiciones.Domain.Constants.EstadoRequisicion;

namespace ObraSync.Requisiciones.Domain.Enums;

/// <summary>
/// Representación tipada de los estados posibles de una requisición de materiales.
/// Mantiene equivalencia directa con <see cref="ConstantesEstado"/> para preservar compatibilidad con BD y contratos JSON.
/// </summary>
public enum EstadoRequisicion
{
    Pendiente = 1,
    Aprobada = 2,
    Rechazada = 3
}

public static class EstadoRequisicionExtensions
{
    public static string ToValorString(this EstadoRequisicion estado) => estado switch
    {
        EstadoRequisicion.Pendiente => ConstantesEstado.Pendiente,
        EstadoRequisicion.Aprobada => ConstantesEstado.Aprobada,
        EstadoRequisicion.Rechazada => ConstantesEstado.Rechazada,
        _ => throw new ArgumentOutOfRangeException(nameof(estado), estado, "Estado de requisición desconocido.")
    };

    public static bool TryParse(string? valor, out EstadoRequisicion estado)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            estado = default;
            return false;
        }

        var normalizado = valor.Trim();
        if (string.Equals(normalizado, ConstantesEstado.Pendiente, StringComparison.OrdinalIgnoreCase))
        {
            estado = EstadoRequisicion.Pendiente;
            return true;
        }

        if (string.Equals(normalizado, ConstantesEstado.Aprobada, StringComparison.OrdinalIgnoreCase))
        {
            estado = EstadoRequisicion.Aprobada;
            return true;
        }

        if (string.Equals(normalizado, ConstantesEstado.Rechazada, StringComparison.OrdinalIgnoreCase))
        {
            estado = EstadoRequisicion.Rechazada;
            return true;
        }

        estado = default;
        return false;
    }

    public static EstadoRequisicion Parse(string? valor)
    {
        if (!TryParse(valor, out var estado))
        {
            throw new ArgumentException($"El estado de requisición '{valor}' no es válido.", nameof(valor));
        }

        return estado;
    }
}
