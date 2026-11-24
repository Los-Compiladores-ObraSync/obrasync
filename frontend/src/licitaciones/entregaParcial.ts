// Entrega parcial y re-despacho (#249). Los campos los fija el contrato de Licitaciones (#248) y el de Recepciones (#236):
// acá solo se leen, no se calculan ni se inventan estados nuevos.
import type { OrdenCompraListadoDto } from './licitacionesTipos'

/** El texto del estado intermedio tal como lo define el contrato de Recepciones (estadoEntrega). */
export const ESTADO_ENTREGA_PARCIAL = 'Entrega parcial / Pendiente de re-despacho'

/** El Proveedor tiene algo para despachar: la orden todavía no salió o salió incompleta y falta el resto. */
export function puedeDespachar(orden: Pick<OrdenCompraListadoDto, 'fueDespachada' | 'puedeRedespachar'>): boolean {
  return !orden.fueDespachada || orden.puedeRedespachar === true
}

/** La última recepción fue parcial y todavía falta recibir parte del pedido. */
export function tieneEntregaParcial(
  orden: Pick<OrdenCompraListadoDto, 'puedeRedespachar' | 'tieneRecepcionParcialPendiente'>,
): boolean {
  return orden.puedeRedespachar === true || orden.tieneRecepcionParcialPendiente === true
}
