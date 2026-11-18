import { createContext } from 'react'
import type { OrdenCompraBloqueada } from '../presupuesto/presupuestoTipos'

export interface BloqueadasContextValue {
  /** Cantidad de órdenes de compra bloqueadas por desvío presupuestario. 0 si el rol no las ve o no hay. */
  cantidad: number
  /** Las órdenes bloqueadas tal como llegaron del servicio. */
  bloqueadas: OrdenCompraBloqueada[]
  /** true cuando ya llegó la primera respuesta. */
  cargado: boolean
  /** true si el último pedido falló. Mientras haya datos viejos se siguen mostrando. */
  huboError: boolean
  /** Vuelve a pedir la lista al servicio. */
  refrescar: () => void
  /** Retira una orden que el backend confirmó que ya no está bloqueada y recarga la lista. */
  reconciliarOrdenNoBloqueada: (ordenCompraId: string) => void
}

/** Valor por defecto: sin proveedor, la cantidad es 0 y refrescar no hace nada.
 *  Permite probar y renderizar los componentes aislados. */
export const BloqueadasContext = createContext<BloqueadasContextValue>({
  cantidad: 0,
  bloqueadas: [],
  // Sin proveedor la campana aislada no espera una consulta que nunca se ejecutará.
  cargado: true,
  huboError: false,
  refrescar: () => {},
  reconciliarOrdenNoBloqueada: () => {},
})
