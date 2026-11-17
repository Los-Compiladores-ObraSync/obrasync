using System.Globalization;
using Microsoft.Extensions.Configuration;
using ObraSync.Licitaciones.Domain.Scoring;

namespace ObraSync.Licitaciones.Application.Services;

public static class ScoringConfigurationExtensions
{
    public static ScoringOptions ReadAndValidateScoringOptions(this IConfiguration? configuration)
    {
        var options = new ScoringOptions();

        if (configuration != null)
        {
            if (TryReadDecimal(configuration["Scoring:PesoPrecio"], out var pesoPrecio))
            {
                options.PesoPrecio = pesoPrecio;
            }

            if (TryReadDecimal(configuration["Scoring:PesoPlazo"], out var pesoPlazo))
            {
                options.PesoPlazo = pesoPlazo;
            }

            if (TryReadDecimal(configuration["Scoring:PesoFlete"], out var pesoFlete))
            {
                options.PesoFlete = pesoFlete;
            }

            if (TryReadDecimal(configuration["Scoring:PesoCalificacion"], out var pesoCalificacion))
            {
                options.PesoCalificacion = pesoCalificacion;
            }

            var modoTexto = configuration["Scoring:ModoNormalizacion"];
            if (!string.IsNullOrWhiteSpace(modoTexto) &&
                Enum.TryParse<ModoNormalizacionScoring>(modoTexto.Trim(), ignoreCase: true, out var modo))
            {
                options.ModoNormalizacion = modo;
            }
        }

        options.Validar();
        return options;
    }

    private static bool TryReadDecimal(string? rawValue, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var normalized = rawValue.Trim().Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }
}
