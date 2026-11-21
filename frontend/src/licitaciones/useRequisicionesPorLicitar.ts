import { useEffect, useState } from 'react'
import { traerTodas } from '../api/paginacion'
import { listarRequisiciones } from '../requisiciones/requisicionesService'
import type { RequisicionResponseDto } from '../requisiciones/requisicionesTipos'
import type { LicitacionResponseDto } from './licitacionesTipos'
import { listarLicitaciones } from './licitacionesService'

/** Una requisición con una licitación en estos estados ya no espera que la publiquen. */
const ESTADOS_YA_LICITADA = ['Abierta', 'Adjudicada']

/**
 * Requisiciones ya aprobadas a las que todavía no se les publicó la licitación (el Comprador es quien
 * tiene que publicarla; hasta entonces los proveedores no pueden cotizar).
 * Si la pantalla ya tiene la lista de licitaciones, se la pasa y no se vuelve a pedir.
 * Si no se pudo consultar devuelve una lista vacía: es un aviso opcional, no una pantalla.
 * Con `habilitado` en false no consulta nada (para roles que no publican licitaciones).
 */
export function useRequisicionesPorLicitar(
  licitacionesConocidas?: LicitacionResponseDto[],
  habilitado = true,
): RequisicionResponseDto[] {
  const [porLicitar, setPorLicitar] = useState<RequisicionResponseDto[]>([])

  useEffect(() => {
    if (!habilitado) return
    let cancelado = false

    async function cargar() {
      try {
        const [aprobadas, licitaciones] = await Promise.all([
          traerTodas((pagina) => listarRequisiciones({ estado: 'Aprobada' }, pagina)),
          licitacionesConocidas ?? listarLicitaciones(),
        ])
        if (cancelado) return
        const yaLicitadas = new Set(
          licitaciones
            .filter((l) => ESTADOS_YA_LICITADA.includes(l.estado))
            .map((l) => l.requisicionId),
        )
        setPorLicitar(aprobadas.filter((r) => !yaLicitadas.has(r.requisicionId)))
      } catch {
        // Aviso opcional: si no se pudo consultar, la pantalla sigue funcionando sin él.
      }
    }

    void cargar()
    return () => {
      cancelado = true
    }
  }, [licitacionesConocidas, habilitado])

  return habilitado ? porLicitar : []
}
