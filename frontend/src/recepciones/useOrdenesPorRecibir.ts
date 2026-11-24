import { useAvisos } from '../notificaciones/useAvisos'

export interface OrdenesPorRecibir {
  /** Pedidos que el Proveedor ya despachó y la obra todavía no recibió por completo (null mientras carga o si no se pudo consultar). */
  cantidad: number | null
}

/** Cuenta los pedidos que ya salieron del proveedor y esperan que el Capataz los reciba: es la misma
 *  lista que muestra "Recibir mercadería" (solo trae órdenes despachadas y no recibidas por completo,
 *  de las obras del Capataz). Lee la lista que ya consulta la campana, sin pedir nada por su cuenta.
 *  Con `activo` en false devuelve null. */
export function useOrdenesPorRecibir(activo: boolean): OrdenesPorRecibir {
  const { datos } = useAvisos()
  const ordenes = activo ? datos?.ordenesPendientes : undefined
  return { cantidad: ordenes ? ordenes.length : null }
}
