import { createContext } from 'react'
import type { Aviso, DatosAvisos } from './avisosFlujo'

export interface AvisosContextValue {
  /** Cantidad de avisos del flujo (para hacer + novedades sin ver). */
  cantidad: number
  /** Lo que hay para hacer: se va solo cuando la persona lo hace. */
  paraHacer: Aviso[]
  /** Lo que pasó y todavía no vio. */
  novedades: Aviso[]
  /** Marca novedades como vistas. Sin argumento marca todas. Lo "para hacer" no se marca: se resuelve haciéndolo. */
  marcarVistas: (cuales?: Aviso[]) => void
  /** Vuelve a consultar los avisos ya, sin esperar al próximo ciclo. Se usa justo después de hacer algo
   *  (despachar, adjudicar…) y al abrir la campana, para que no quede un aviso de algo ya resuelto.
   *  Con `esperaMinimaMs` no vuelve a consultar si la última consulta fue hace menos que eso. */
  refrescar: (esperaMinimaMs?: number) => void
  /** Las listas que devolvió la última consulta (null mientras no hay ninguna). Las tarjetas del Inicio
   *  cuentan desde acá, así no repiten los pedidos y muestran el mismo número que la campana. */
  datos: DatosAvisos | null
}

/** Valor por defecto: sin proveedor no hay avisos. Así la campana se puede probar sola. */
export const AvisosContext = createContext<AvisosContextValue>({
  cantidad: 0,
  paraHacer: [],
  novedades: [],
  marcarVistas: () => {},
  refrescar: () => {},
  datos: null,
})
