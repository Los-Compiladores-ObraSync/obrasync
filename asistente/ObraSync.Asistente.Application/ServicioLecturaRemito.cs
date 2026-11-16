namespace ObraSync.Asistente.Application;

/// <summary>
/// Lectura de remitos por foto (#209). Hace lo mismo que el agente antes de llamar al modelo: respeta el control de
/// consumo (el cupo es compartido por todo el grupo) y reporta 429/403. Después LIMPIA lo que dijo el modelo: lo que
/// salga de la foto es un dato sugerido, nunca algo de lo que haya que fiarse.
/// Solo lee: no guarda la imagen ni escribe nada.
/// </summary>
public sealed class ServicioLecturaRemito
{
    public const int MaxLargoRemito = 50;

    /// <summary>El backend de Recepción acepta hasta 1 día de tolerancia por zona horaria (#127); acá se usa la misma.</summary>
    public const int DiasDeToleranciaFutura = 1;

    /// <summary>Un remito más viejo que esto no se toma como fecha leída (suele ser un dígito mal leído).</summary>
    public const int AniosMaximosAtras = 5;

    private static readonly TimeSpan TiempoTotal = TimeSpan.FromSeconds(30);

    private readonly IExtractorRemito _extractor;
    private readonly IControlConsumo? _consumo;
    private readonly Func<DateOnly> _hoy;

    public ServicioLecturaRemito(IExtractorRemito extractor, IControlConsumo? consumo = null, Func<DateOnly>? hoy = null)
    {
        _extractor = extractor;
        _consumo = consumo;
        _hoy = hoy ?? (() => DateOnly.FromDateTime(DateTime.UtcNow));
    }

    public async Task<ResultadoLecturaRemito> LeerAsync(byte[] imagen, string tipoMedia, CancellationToken ct)
    {
        var decision = _consumo?.Autorizar() ?? DecisionConsumo.Permitido;
        if (decision != DecisionConsumo.Permitido)
            return ResultadoLecturaRemito.Falla(decision switch
            {
                DecisionConsumo.Apagado => "asistente_apagado",
                DecisionConsumo.SinCupo => "asistente_sin_cupo",
                _ => "asistente_ocupado"
            });

        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TiempoTotal);

        LecturaCruda cruda;
        try
        {
            cruda = await _extractor.ExtraerAsync(imagen, tipoMedia, limite.Token);
        }
        catch (ModeloException ex)
        {
            if (ex.Codigo == "modelo_limite_de_tasa")
                _consumo?.ReportarLimiteDeTasa();
            else if (ex.Codigo == "modelo_sin_permiso_o_cupo")
                _consumo?.ReportarSinCupo();
            return ResultadoLecturaRemito.Falla(ex.Codigo);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ResultadoLecturaRemito.Falla("modelo_timeout");
        }

        _consumo?.Registrar(cruda.Tokens);
        return ResultadoLecturaRemito.Bien(
            new LecturaRemito(LimpiarNumero(cruda.NumeroRemito), LimpiarFecha(cruda.Fecha)), cruda.Tokens);
    }

    /// <summary>Número sin espacios de más; null si está vacío o es más largo de lo que acepta Recepción.</summary>
    private static string? LimpiarNumero(string? numero)
    {
        var limpio = numero?.Trim();
        if (string.IsNullOrEmpty(limpio) || limpio.Length > MaxLargoRemito)
            return null;
        // Un número de remito no tiene saltos de línea ni caracteres de control: si aparecen, no es una lectura confiable.
        return limpio.Any(char.IsControl) ? null : limpio;
    }

    /// <summary>Solo vale una fecha yyyy-MM-dd real, no futura (con la tolerancia de #127) ni absurdamente vieja.</summary>
    private DateOnly? LimpiarFecha(string? fecha)
    {
        if (!DateOnly.TryParseExact(fecha?.Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var valor))
            return null;
        var hoy = _hoy();
        if (valor > hoy.AddDays(DiasDeToleranciaFutura) || valor < hoy.AddYears(-AniosMaximosAtras))
            return null;
        return valor;
    }
}
