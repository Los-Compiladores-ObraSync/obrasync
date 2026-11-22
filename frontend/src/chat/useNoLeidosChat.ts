import { useCallback, useEffect, useState } from 'react'
import { INTERVALO_NO_LEIDOS_MS } from './chatConfig'
import { obtenerNoLeidos } from './chatService'

export interface NoLeidosChat {
  /** Mensajes sin leer de una orden. "inicial" es lo que traía la lista de órdenes; vale hasta la primera consulta. */
  cantidadDe: (ordenCompraId: string, inicial?: number) => number
  /** Deja en 0 el contador de una orden (al abrir su conversación). */
  marcarLeida: (ordenCompraId: string) => void
}

/** Contador de mensajes sin leer por orden para las listas de órdenes (#225). Consulta al abrirse, cada 30 segundos
 *  y al volver a la pestaña; no consulta con la pestaña oculta. Si falla, queda lo último que se supo. */
export function useNoLeidosChat(intervaloMs: number = INTERVALO_NO_LEIDOS_MS): NoLeidosChat {
  const [porOrden, setPorOrden] = useState<Record<string, number> | null>(null)

  useEffect(() => {
    let cancelado = false

    async function pedir() {
      try {
        const resumen = await obtenerNoLeidos()
        if (cancelado) return
        setPorOrden(Object.fromEntries(resumen.ordenes.map((o) => [o.ordenCompraId, o.mensajesNoLeidos])))
      } catch {
        // Es un número de apoyo: si no se pudo consultar, se conserva el último.
      }
    }

    void pedir()
    const temporizador = window.setInterval(() => {
      if (!document.hidden) void pedir()
    }, intervaloMs)
    function alCambiarVisibilidad() {
      if (!document.hidden) void pedir()
    }
    document.addEventListener('visibilitychange', alCambiarVisibilidad)

    return () => {
      cancelado = true
      window.clearInterval(temporizador)
      document.removeEventListener('visibilitychange', alCambiarVisibilidad)
    }
  }, [intervaloMs])

  const cantidadDe = useCallback(
    (ordenCompraId: string, inicial = 0) => (porOrden === null ? inicial : (porOrden[ordenCompraId] ?? 0)),
    [porOrden],
  )

  const marcarLeida = useCallback((ordenCompraId: string) => {
    setPorOrden((actual) => ({ ...(actual ?? {}), [ordenCompraId]: 0 }))
  }, [])

  return { cantidadDe, marcarLeida }
}
