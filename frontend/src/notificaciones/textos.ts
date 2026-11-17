/** Texto del aviso de pendientes, en singular cuando hay una sola.
 *  Lo usan el panel de la campana y la tarjeta de la pantalla principal del Comprador. */
export function textoPendientes(cantidad: number): string {
  return cantidad === 1
    ? 'Tenés 1 requisición para aprobar'
    : `Tenés ${cantidad} requisiciones para aprobar`
}

/** Texto del aviso de órdenes de compra bloqueadas por presupuesto (#97).
 *  Contempla singular y plural. */
export function textoOrdenesBloqueadas(cantidad: number): string {
  return cantidad === 1
    ? '1 orden de compra bloqueada por presupuesto'
    : `${cantidad} órdenes de compra bloqueadas por presupuesto`
}

export const textoBloqueadas = textoOrdenesBloqueadas
