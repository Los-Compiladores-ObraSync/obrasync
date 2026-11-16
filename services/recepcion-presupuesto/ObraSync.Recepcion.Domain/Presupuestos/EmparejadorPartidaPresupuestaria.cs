using System.Collections.Generic;
using System.Linq;
using ObraSync.Recepcion.Domain.Entities;

namespace ObraSync.Recepcion.Domain.Presupuestos;

/// <summary>
/// Servicio de dominio para emparejar una orden de compra con su partida presupuestaria (#196).
/// Aplica normalización de etapa y respaldo (fallback) por partida general de toda la obra.
/// </summary>
public static class EmparejadorPartidaPresupuestaria
{
    /// <summary>
    /// Busca la partida presupuestaria correspondiente según la etapa y la obra:
    /// 1. Coincidencia por etapa normalizada (sin distinguir mayúsculas, espacios ni tildes).
    /// 2. Respaldo por partida de toda la obra (Etapa nula o en blanco) si no existe partida para la etapa especificada.
    /// 3. Retorna null si no existe partida específica ni de respaldo general.
    /// </summary>
    public static PartidaPresupuestaria? BuscarPartida(
        IEnumerable<PartidaPresupuestaria>? partidasObra,
        string? etapaOrden)
    {
        if (partidasObra is null)
            return null;

        var lista = partidasObra as IList<PartidaPresupuestaria> ?? partidasObra.ToList();

        // 1. Si la orden tiene etapa especificada, buscar coincidencia por etapa normalizada
        var etapaNormalizada = NormalizadorEtapa.Normalizar(etapaOrden);
        if (etapaNormalizada is not null)
        {
            var partidaEtapa = lista.FirstOrDefault(p =>
                NormalizadorEtapa.SonEquivalentes(p.Etapa, etapaOrden));

            if (partidaEtapa is not null)
                return partidaEtapa;
        }

        // 2. Respaldo (fallback): partida de toda la obra (Etapa nula o solo espacios)
        var partidaGeneralObra = lista.FirstOrDefault(p =>
            string.IsNullOrWhiteSpace(p.Etapa));

        return partidaGeneralObra;
    }
}
