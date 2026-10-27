import { createContext } from 'react'

export interface PendientesContextValue {
  /** Requisiciones pendientes de aprobar. 0 si el rol no las aprueba. */
  cantidad: number
  /** Vuelve a pedir la cantidad al servidor (por ejemplo después de aprobar una). */
  refrescar: () => void
}

/** Valor por defecto: sin proveedor, la cantidad es 0 y refrescar no hace nada.
 *  Así las pantallas que lo usan también se pueden probar solas. */
export const PendientesContext = createContext<PendientesContextValue>({
  cantidad: 0,
  refrescar: () => {},
})
