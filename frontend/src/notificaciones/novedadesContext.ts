import { createContext } from 'react'
import type { Novedad } from './novedades'

export interface NovedadesContextValue {
  /** Cantidad de avisos propios sin ver. 0 si el rol no tiene (Comprador, Admin). */
  cantidad: number
  /** Los avisos sin ver, los más recientes primero. */
  novedades: Novedad[]
  /** Marca avisos como vistos y no vuelven a aparecer. Sin argumento marca todos. */
  marcarVistas: (cuales?: Novedad[]) => void
}

/** Valor por defecto: sin proveedor no hay avisos. Así la campana se puede probar sola. */
export const NovedadesContext = createContext<NovedadesContextValue>({
  cantidad: 0,
  novedades: [],
  marcarVistas: () => {},
})
