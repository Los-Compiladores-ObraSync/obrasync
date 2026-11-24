import { useEffect, useState } from 'react'
import { obtenerOrdenesPendientes } from './recepcionesService'

export interface OrdenesPorRecibir {
  /** Pedidos que el Proveedor ya despachó y la obra todavía no recibió por completo (null mientras carga o si falló). */
  cantidad: number | null
  huboError: boolean
}

/** Cuenta los pedidos que ya salieron del proveedor y esperan que el Capataz los reciba: es la misma
 *  lista que muestra "Recibir mercadería" (solo trae órdenes despachadas y no recibidas por completo,
 *  de las obras del Capataz). Con `activo` en false no pide nada. */
export function useOrdenesPorRecibir(activo: boolean): OrdenesPorRecibir {
  const [estado, setEstado] = useState<OrdenesPorRecibir>({ cantidad: null, huboError: false })

  useEffect(() => {
    if (!activo) return
    let cancelado = false

    obtenerOrdenesPendientes()
      .then((ordenes) => {
        if (!cancelado) setEstado({ cantidad: ordenes.length, huboError: false })
      })
      .catch(() => {
        if (!cancelado) setEstado({ cantidad: null, huboError: true })
      })

    return () => {
      cancelado = true
    }
  }, [activo])

  return estado
}
