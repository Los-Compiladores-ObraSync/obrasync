import type { CotizacionResponseDto, LicitacionResponseDto } from './licitacionesTipos'

/**
 * Calcula el precio total de una cotización:
 * Suma de (precioUnitario × cantidad) de cada ítem, tomando la cantidad del pliego de la licitación.
 */
export function calcularPrecioTotalCotizacion(
  cotizacion: Pick<CotizacionResponseDto, 'items'>,
  licitacion: Pick<LicitacionResponseDto, 'items'>,
): number {
  const mapaCantidades = new Map(
    licitacion.items.map((it) => [it.articuloId, it.cantidad]),
  )
  return cotizacion.items.reduce((total, it) => {
    const cantidad = mapaCantidades.get(it.articuloId) ?? 0
    return total + it.precioUnitario * cantidad
  }, 0)
}

/** Precio total de la cotización más el flete (el flete no está incluido en "precio total"). */
export function calcularTotalConFlete(
  cotizacion: Pick<CotizacionResponseDto, 'items' | 'flete'>,
  licitacion: Pick<LicitacionResponseDto, 'items'>,
): number {
  return calcularPrecioTotalCotizacion(cotizacion, licitacion) + (cotizacion.flete ?? 0)
}

export interface LineaDeCotizacion {
  articuloId: string
  descripcion: string
  cantidad: number
  unidadMedida: string
  /** undefined si el proveedor no cotizó este material. */
  precioUnitario?: number
  /** precioUnitario × cantidad (0 si no cotizó el material). */
  subtotal: number
}

/** Una línea por cada material del pliego, con lo que cotizó el proveedor (si cotizó). */
export function desglosarCotizacion(
  cotizacion: Pick<CotizacionResponseDto, 'items'>,
  licitacion: Pick<LicitacionResponseDto, 'items'>,
): LineaDeCotizacion[] {
  const precios = new Map(cotizacion.items.map((it) => [it.articuloId, it.precioUnitario]))
  return licitacion.items.map((material) => {
    const precioUnitario = precios.get(material.articuloId)
    return {
      articuloId: material.articuloId,
      descripcion: material.descripcion,
      cantidad: material.cantidad,
      unidadMedida: material.unidadMedida,
      precioUnitario,
      subtotal: (precioUnitario ?? 0) * material.cantidad,
    }
  })
}

export type MarcaDeComparacion = 'Más barata' | 'Flete más bajo' | 'Entrega más rápida'

/**
 * Qué cotizaciones son las mejores en costo total (precio + flete), flete y plazo, para marcarlas en pantalla.
 * Un flete sin informar cuenta como 0 y un plazo sin informar como "inmediato" (igual que se muestran).
 * Solo se marca un criterio si hay al menos dos cotizaciones y no están todas empatadas
 * (si todas valen lo mismo, la marca no ayuda a elegir). Si hay empate en lo mejor, todas llevan la marca.
 */
export function marcasDeComparacion(
  cotizaciones: Array<Pick<CotizacionResponseDto, 'cotizacionId' | 'items' | 'flete' | 'plazoEntregaDias'>>,
  licitacion: Pick<LicitacionResponseDto, 'items'>,
): Map<string, MarcaDeComparacion[]> {
  const marcas = new Map<string, MarcaDeComparacion[]>(
    cotizaciones.map((c) => [c.cotizacionId, []]),
  )
  if (cotizaciones.length < 2) return marcas

  const criterios: Array<[MarcaDeComparacion, (c: (typeof cotizaciones)[number]) => number]> = [
    // "Más barata" = menor precio + flete, igual que esMasBarata del servicio (CU-07).
    ['Más barata', (c) => calcularTotalConFlete(c, licitacion)],
    ['Flete más bajo', (c) => c.flete ?? 0],
    ['Entrega más rápida', (c) => c.plazoEntregaDias ?? 0],
  ]
  for (const [marca, valorDe] of criterios) {
    const valores = cotizaciones.map(valorDe)
    const mejor = Math.min(...valores)
    if (mejor === Math.max(...valores)) continue
    cotizaciones.forEach((c, i) => {
      if (valores[i] === mejor) marcas.get(c.cotizacionId)?.push(marca)
    })
  }
  return marcas
}

/**
 * Formatea un número como importe con "$", sin código de moneda ARS.
 * Ej: 850000 -> "$ 850.000"
 */
export function formatoPrecio(valor: number): string {
  return `$ ${valor.toLocaleString('es-AR')}`
}

/**
 * Ordena las ofertas de la de mayor a menor calificación (puntajeTotal), para que la mejor quede
 * arriba aunque Compras cambie los pesos. El servicio las manda por fecha de cotización.
 * Mismo desempate que usa el servicio para elegir la oferta destacada: entrega más rápida y,
 * si sigue empatado, la que se cotizó antes. La oferta destacada va siempre primera.
 * Devuelve una copia: no modifica la lista original.
 */
export function ordenarPorCalificacion(
  cotizaciones: CotizacionResponseDto[],
): CotizacionResponseDto[] {
  return [...cotizaciones].sort((a, b) => {
    if (a.esGanadora !== b.esGanadora) return a.esGanadora ? -1 : 1
    if (a.puntajeTotal !== b.puntajeTotal) return b.puntajeTotal - a.puntajeTotal
    const plazoA = a.plazoEntregaDias ?? Number.POSITIVE_INFINITY
    const plazoB = b.plazoEntregaDias ?? Number.POSITIVE_INFINITY
    if (plazoA !== plazoB) return plazoA < plazoB ? -1 : 1
    const fechaA = Date.parse(a.fechaCreacion)
    const fechaB = Date.parse(b.fechaCreacion)
    if (fechaA !== fechaB) return fechaA - fechaB
    return a.cotizacionId.localeCompare(b.cotizacionId)
  })
}

/** Pasa un puntaje de 0 a 100 a estrellas enteras de 0 a 5 (cada estrella son 20 puntos, redondeando). */
export function estrellasDePuntaje(puntaje: number): number {
  if (!Number.isFinite(puntaje)) return 0
  return Math.min(5, Math.max(0, Math.round(puntaje / 20)))
}

/** Último precio unitario que se pagó por un artículo (de GET /api/ordenes-compra/precios-historicos). */
export interface PrecioPrevio {
  articuloId: string
  ultimoPrecioUnitario: number
  fechaEmision: string
}

/**
 * Compara el precio cotizado con el último que se pagó por el mismo artículo.
 * Devuelve la diferencia en % (positivo = más caro que antes) y un texto corto, o null si no hay con qué comparar.
 * Un cambio de menos de 0,5 % se considera "igual": no vale la pena marcarlo.
 */
export function compararConPrecioPrevio(
  precioCotizado: number,
  previo: Pick<PrecioPrevio, 'ultimoPrecioUnitario'> | undefined,
): { porcentaje: number; texto: string } | null {
  if (!previo || !(previo.ultimoPrecioUnitario > 0) || !Number.isFinite(precioCotizado)) return null
  const porcentaje = ((precioCotizado - previo.ultimoPrecioUnitario) / previo.ultimoPrecioUnitario) * 100
  const redondeado = Math.round(Math.abs(porcentaje))
  if (Math.abs(porcentaje) < 0.5) return { porcentaje, texto: 'igual que la última compra' }
  return {
    porcentaje,
    texto: porcentaje > 0 ? `${redondeado} % más caro que la última compra` : `${redondeado} % más barato que la última compra`,
  }
}
