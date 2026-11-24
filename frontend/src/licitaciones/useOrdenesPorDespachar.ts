import { useAvisos } from '../notificaciones/useAvisos'
import { puedeDespachar } from './entregaParcial'

export interface OrdenesPorDespachar {
  /** Órdenes que el proveedor tiene que despachar (null mientras carga o si no se pudo consultar). */
  cantidad: number | null
}

/** Cuenta las órdenes que el proveedor todavía tiene que despachar: las que no despachó nunca y las
 *  que llegaron parciales y tienen un remanente para volver a despachar (#249). Es la misma regla
 *  que usa "Mis órdenes" para mostrar el botón de despachar. Lee la lista que ya consulta la campana
 *  (no pide nada por su cuenta), así el número de la tarjeta y el de la campana siempre coinciden.
 *  Con `activo` en false devuelve null. */
export function useOrdenesPorDespachar(activo: boolean): OrdenesPorDespachar {
  const { datos } = useAvisos()
  const ordenes = activo ? datos?.ordenesCompra : undefined
  return { cantidad: ordenes ? ordenes.filter(puedeDespachar).length : null }
}
