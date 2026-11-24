import type { ItemOrdenPendiente } from './recepcionesTipos'

export function saldoPendiente(item: ItemOrdenPendiente): number {
  return item.cantidadPendiente ?? Math.max(item.cantidadEsperada - item.cantidadRecibida, 0)
}
