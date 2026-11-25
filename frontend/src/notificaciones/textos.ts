import type { Novedad } from './novedades'

/** Texto del aviso de pendientes, en singular cuando hay una sola.
 *  Lo usan el panel de la campana y la tarjeta de la pantalla principal del Comprador. */
export function textoPendientes(cantidad: number): string {
  return cantidad === 1
    ? 'Tenés 1 requisición para aprobar'
    : `Tenés ${cantidad} requisiciones para aprobar`
}

/** Texto de la tarjeta de la pantalla principal del Proveedor: licitaciones abiertas que le faltan
 *  cotizar, en singular cuando hay una sola. */
export function textoPedidosParaRecibir(cantidad: number): string {
  return cantidad === 1 ? 'Tenés 1 pedido para recibir' : `Tenés ${cantidad} pedidos para recibir`
}

export function textoOrdenesParaDespachar(cantidad: number): string {
  return cantidad === 1 ? 'Tenés 1 orden para despachar' : `Tenés ${cantidad} órdenes para despachar`
}

export function textoLicitacionesParaCotizar(cantidad: number): string {
  return cantidad === 1
    ? 'Tenés 1 licitación para cotizar'
    : `Tenés ${cantidad} licitaciones para cotizar`
}

/** Texto del aviso de órdenes de compra bloqueadas por presupuesto (#97).
 *  Contempla singular y plural. */
export function textoOrdenesBloqueadas(cantidad: number): string {
  return cantidad === 1
    ? '1 orden de compra bloqueada por presupuesto'
    : `${cantidad} órdenes de compra bloqueadas por presupuesto`
}

/** Título del aviso de novedades del Capataz y del Proveedor (#163). Singular cuando hay una sola. */
export function textoNovedades(novedades: Novedad[]): string {
  const cantidad = novedades.length
  if (novedades.every((n) => n.tipo === 'licitacion-nueva')) {
    return cantidad === 1
      ? 'Hay 1 licitación nueva para cotizar'
      : `Hay ${cantidad} licitaciones nuevas para cotizar`
  }
  return cantidad === 1
    ? 'Tenés 1 novedad en tus requisiciones'
    : `Tenés ${cantidad} novedades en tus requisiciones`
}

/** Renglón de cada novedad en el panel. */
export function textoDeNovedad(novedad: Novedad): string {
  switch (novedad.tipo) {
    case 'requisicion-aprobada':
      return `Tu requisición ${novedad.numero} fue aprobada`
    case 'requisicion-rechazada':
      return `Tu requisición ${novedad.numero} fue rechazada`
    case 'licitacion-nueva':
      return `Licitación abierta: ${novedad.numero}`
  }
}
