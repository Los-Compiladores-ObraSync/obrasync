import { createContext } from 'react'
import type { RequisicionResponseDto } from '../requisiciones/requisicionesTipos'

export interface PendientesContextValue {
  /** Requisiciones pendientes de aprobar. 0 si el rol no las aprueba. */
  cantidad: number
  /** Las requisiciones pendientes tal como llegaron del servidor (sin ordenar). */
  pendientes: RequisicionResponseDto[]
  /** true cuando ya llegó la primera respuesta. Antes de eso `pendientes` está vacía
   *  porque todavía no se sabe, no porque no haya nada. */
  cargado: boolean
  /** true si el último pedido falló. Mientras haya datos viejos se siguen mostrando. */
  huboError: boolean
  /** Vuelve a pedir la lista al servidor (por ejemplo después de aprobar una). */
  refrescar: () => void
}

/** Valor por defecto: sin proveedor, la cantidad es 0 y refrescar no hace nada.
 *  Así las pantallas que lo usan también se pueden probar solas. */
export const PendientesContext = createContext<PendientesContextValue>({
  cantidad: 0,
  pendientes: [],
  cargado: false,
  huboError: false,
  refrescar: () => {},
})
