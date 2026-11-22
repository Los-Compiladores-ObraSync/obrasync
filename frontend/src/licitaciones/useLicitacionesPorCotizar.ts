import { useEffect, useState } from 'react'
import { listarLicitaciones } from './licitacionesService'

export interface LicitacionesPorCotizar {
  /** Licitaciones abiertas que el proveedor todavía no cotizó (null mientras carga o si falló). */
  cantidad: number | null
  huboError: boolean
}

/** Cuenta las licitaciones abiertas que el proveedor aún no cotizó. La usan la tarjeta del Inicio y
 *  el numerito del botón "Licitaciones abiertas", así la lista se pide una sola vez.
 *  Con `activo` en false no pide nada (los demás roles no cotizan). */
export function useLicitacionesPorCotizar(activo: boolean): LicitacionesPorCotizar {
  const [estado, setEstado] = useState<LicitacionesPorCotizar>({ cantidad: null, huboError: false })

  useEffect(() => {
    if (!activo) return
    let cancelado = false

    listarLicitaciones()
      .then((licitaciones) => {
        if (cancelado) return
        setEstado({
          cantidad: licitaciones.filter((l) => l.yaCotizada !== true).length,
          huboError: false,
        })
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
