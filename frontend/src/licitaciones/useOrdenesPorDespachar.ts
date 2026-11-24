import { useEffect, useState } from 'react'
import { puedeDespachar } from './entregaParcial'
import { listarOrdenesCompra } from './licitacionesService'

export interface OrdenesPorDespachar {
  /** Órdenes que el proveedor tiene que despachar (null mientras carga o si falló). */
  cantidad: number | null
  huboError: boolean
}

/** Cuenta las órdenes que el proveedor todavía tiene que despachar: las que no despachó nunca y las
 *  que llegaron parciales y tienen un remanente para volver a despachar (#249). Es la misma regla
 *  que usa "Mis órdenes" para mostrar el botón de despachar. Con `activo` en false no pide nada. */
export function useOrdenesPorDespachar(activo: boolean): OrdenesPorDespachar {
  const [estado, setEstado] = useState<OrdenesPorDespachar>({ cantidad: null, huboError: false })

  useEffect(() => {
    if (!activo) return
    let cancelado = false

    listarOrdenesCompra()
      .then((ordenes) => {
        if (cancelado) return
        setEstado({ cantidad: ordenes.filter(puedeDespachar).length, huboError: false })
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
