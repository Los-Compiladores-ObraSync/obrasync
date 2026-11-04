/** Texto del aviso de pendientes, en singular cuando hay una sola.
 *  Lo usan el panel de la campana y la tarjeta de la pantalla principal del Comprador. */
export function textoPendientes(cantidad: number): string {
  return cantidad === 1
    ? 'Tenés 1 requisición para aprobar'
    : `Tenés ${cantidad} requisiciones para aprobar`
}
