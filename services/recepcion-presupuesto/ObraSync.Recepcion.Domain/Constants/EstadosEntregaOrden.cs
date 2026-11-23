namespace ObraSync.Recepcion.Domain.Constants;

/// <summary>
/// Estados intermedios y finales de entrega de una orden de compra expuestos en el contrato (#236).
/// </summary>
public static class EstadosEntregaOrden
{
    public const string Pendiente = "Pendiente";
    public const string EntregaParcial = "Entrega parcial / Pendiente de re-despacho";
    public const string ConProblema = "Con problema";
    public const string Completa = "Completa";

    public static string Calcular(bool estaCompleta, bool tieneProblemaAbierto, bool tieneRecepcionParcial)
    {
        if (estaCompleta)
        {
            return Completa;
        }

        if (tieneProblemaAbierto)
        {
            return ConProblema;
        }

        if (tieneRecepcionParcial)
        {
            return EntregaParcial;
        }

        return Pendiente;
    }
}
