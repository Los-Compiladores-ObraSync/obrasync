namespace ObraSync.Asistente.Application;

/// <summary>Qué decide el control de consumo antes de llamar al modelo.</summary>
public enum DecisionConsumo
{
    /// <summary>Se puede llamar al modelo.</summary>
    Permitido,

    /// <summary>Estamos cerca del tope por minuto (o el gateway de IA acaba de responder 429): hay que esperar.</summary>
    Ocupado,

    /// <summary>Se agotó el cupo (tope mensual o el gateway de IA respondió 403 por cuota): no se llama al modelo.</summary>
    SinCupo,

    /// <summary>El interruptor de configuración dejó el asistente apagado.</summary>
    Apagado
}

/// <summary>Foto del consumo, para el endpoint de estado. No incluye ningún dato de usuarios ni mensajes.</summary>
public sealed record EstadoConsumo(
    bool Habilitado,
    bool SinCupoPorCuota,
    bool EnEspera,
    long TokensUltimoMinuto,
    int TopeMinuto,
    long TokensMes,
    long TopeMes,
    double PorcentajeMes,
    bool AvisoMensualEmitido);

/// <summary>
/// Cuenta los tokens que gasta el asistente (el cupo es compartido por TODO el grupo) y decide si se puede
/// llamar al modelo. Al ser una interfaz, en las pruebas se usa uno falso o uno con reloj controlado.
/// </summary>
public interface IControlConsumo
{
    /// <summary>Se llama ANTES de preguntarle al modelo. Si no es Permitido, no se debe llamar al modelo.</summary>
    DecisionConsumo Autorizar();

    /// <summary>Se llama DESPUÉS de cada respuesta del modelo con los tokens que informó.</summary>
    void Registrar(int tokens);

    /// <summary>El gateway de IA respondió 429: se pausa un rato y no se reintenta en bucle.</summary>
    void ReportarLimiteDeTasa();

    /// <summary>El gateway de IA respondió 403 (cuota de costo): el asistente queda apagado hasta reiniciar el servicio.</summary>
    void ReportarSinCupo();

    EstadoConsumo Estado();
}
