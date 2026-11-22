import { useCallback, useState } from 'react'
import type { OrdenCompraListadoDto } from '../licitaciones/licitacionesTipos'
import type { EstadoProblema } from './chatTipos'
import { useNoLeidosChat } from './useNoLeidosChat'

/**
 * Lo que comparten las listas de órdenes (Compras y Proveedor) para el chat (#225): el contador de no leídos
 * por orden y el estado de la entrega con problema, que el chat puede cambiar (tiempo real o "Marcar como resuelto").
 */
export function useChatEnLista() {
  const { cantidadDe, marcarLeida } = useNoLeidosChat()
  const [problemas, setProblemas] = useState<Record<string, EstadoProblema>>({})

  const noLeidosDe = useCallback(
    (orden: OrdenCompraListadoDto) => cantidadDe(orden.id, orden.mensajesNoLeidos ?? 0),
    [cantidadDe],
  )

  /** El problema que se muestra: lo último que supo el chat o, si no se abrió, lo que trajo la lista. */
  const problemaDe = useCallback(
    (orden: OrdenCompraListadoDto): EstadoProblema =>
      problemas[orden.id] ?? { abierto: orden.tieneProblemaAbierto === true, motivo: orden.motivoProblemaAbierto ?? null },
    [problemas],
  )

  const cambiarProblema = useCallback((ordenCompraId: string, estado: EstadoProblema) => {
    setProblemas((actuales) => {
      const previo = actuales[ordenCompraId]
      if (previo && previo.abierto === estado.abierto && previo.motivo === estado.motivo) return actuales
      return { ...actuales, [ordenCompraId]: estado }
    })
  }, [])

  return { noLeidosDe, problemaDe, marcarLeida, cambiarProblema }
}
