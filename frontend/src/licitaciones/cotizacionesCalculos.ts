import type {
  CotizacionResponseDto,
  CrearCotizacionItemDto,
  LicitacionResponseDto,
} from './licitacionesTipos'

/**
 * Total comparable de un renglón, con la misma fórmula del contrato (#190):
 * (cantidad ofrecida, o la del pliego si no hay otra) × precio unitario.
 * Si el servicio ya mandó el subtotal del renglón, se usa ese.
 */
export function subtotalDeItem(
  item: Pick<CrearCotizacionItemDto, 'precioUnitario' | 'cantidadOfrecida' | 'subtotalItem'>,
  cantidadPliego: number,
): number {
  if (item.subtotalItem !== undefined) return item.subtotalItem
  return (item.cantidadOfrecida ?? cantidadPliego) * item.precioUnitario
}

/** true si la oferta propone otra presentación en algún ítem (#229). */
export function tienePresentacionDistinta(cotizacion: Pick<CotizacionResponseDto, 'items'>): boolean {
  return cotizacion.items.some((it) => Boolean(it.presentacionOfrecida?.trim()))
}

/** true si el servicio marcó que la oferta no cubre todo lo pedido (#190). La marca la calcula el servicio. */
export function tieneCoberturaParcial(
  cotizacion: Pick<CotizacionResponseDto, 'items' | 'coberturaParcial'>,
): boolean {
  return Boolean(cotizacion.coberturaParcial) || cotizacion.items.some((it) => it.coberturaParcial)
}

/**
 * Calcula el precio total de una cotización:
 * Suma del total de cada ítem: precioUnitario × cantidad, tomando la cantidad del pliego de la licitación
 * (o la cantidad ofrecida si el Proveedor ofreció otra presentación, #229).
 */
export function calcularPrecioTotalCotizacion(
  cotizacion: Pick<CotizacionResponseDto, 'items'>,
  licitacion: Pick<LicitacionResponseDto, 'items'>,
): number {
  const mapaCantidades = new Map(
    licitacion.items.map((it) => [it.articuloId, it.cantidad]),
  )
  return cotizacion.items.reduce((total, it) => {
    const cantidad = mapaCantidades.get(it.articuloId)
    if (cantidad === undefined) return total
    return total + subtotalDeItem(it, cantidad)
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
  /** precioUnitario × cantidad (o × cantidad ofrecida si hay otra presentación; 0 si no cotizó el material). */
  subtotal: number
  /** Otra presentación que ofreció el Proveedor para este material (#229). */
  presentacionOfrecida?: string
  /** Cantidad que ofreció en esa presentación. */
  cantidadOfrecida?: number
  /** true si el servicio marcó que este renglón no cubre todo lo pedido. */
  coberturaParcial?: boolean
}

/** Una línea por cada material del pliego, con lo que cotizó el proveedor (si cotizó). */
export function desglosarCotizacion(
  cotizacion: Pick<CotizacionResponseDto, 'items'>,
  licitacion: Pick<LicitacionResponseDto, 'items'>,
): LineaDeCotizacion[] {
  const ofertas = new Map(cotizacion.items.map((it) => [it.articuloId, it]))
  return licitacion.items.map((material) => {
    const oferta = ofertas.get(material.articuloId)
    return {
      articuloId: material.articuloId,
      descripcion: material.descripcion,
      cantidad: material.cantidad,
      unidadMedida: material.unidadMedida,
      precioUnitario: oferta?.precioUnitario,
      subtotal: oferta ? subtotalDeItem(oferta, material.cantidad) : 0,
      presentacionOfrecida: oferta?.presentacionOfrecida?.trim() || undefined,
      cantidadOfrecida: oferta?.cantidadOfrecida ?? undefined,
      coberturaParcial: oferta?.coberturaParcial,
    }
  })
}

export type MarcaDeComparacion = 'Más barata' | 'Flete más bajo' | 'Entrega más rápida'

export type OrdenCotizaciones =
  | 'mejor-puntaje'
  | 'precio-asc'
  | 'precio-desc'
  | 'flete-asc'
  | 'flete-desc'
  | 'plazo-asc'
  | 'plazo-desc'
  | 'calificacion-desc'
  | 'calificacion-asc'

/** Ordena localmente ofertas cargadas; los empates del criterio se resuelven por puntaje y luego ID. */
export function ordenarCotizaciones(
  cotizaciones: CotizacionResponseDto[],
  orden: OrdenCotizaciones,
  licitacion: Pick<LicitacionResponseDto, 'items'>,
): CotizacionResponseDto[] {
  return [...cotizaciones].sort((a, b) => {
    if (orden === 'mejor-puntaje') {
      return b.puntajeTotal - a.puntajeTotal || a.cotizacionId.localeCompare(b.cotizacionId)
    }

    if (orden === 'calificacion-desc' || orden === 'calificacion-asc') {
      const calificacionA = a.calificacionProveedor
      const calificacionB = b.calificacionProveedor
      if (calificacionA == null && calificacionB != null) return 1
      if (calificacionA != null && calificacionB == null) return -1
      if (calificacionA != null && calificacionB != null && calificacionA !== calificacionB) {
        return orden === 'calificacion-desc'
          ? calificacionB - calificacionA
          : calificacionA - calificacionB
      }
    } else {
      const ascendente = orden.endsWith('-asc')
      const valor = (cotizacion: CotizacionResponseDto) => {
        switch (orden) {
          case 'precio-asc':
          case 'precio-desc':
            return cotizacion.subtotalItems ?? calcularPrecioTotalCotizacion(cotizacion, licitacion)
          case 'flete-asc':
          case 'flete-desc':
            return cotizacion.flete ?? 0
          case 'plazo-asc':
          case 'plazo-desc':
            return cotizacion.plazoEntregaDias ?? 0
          default:
            return 0
        }
      }
      const diferencia = valor(a) - valor(b)
      if (diferencia !== 0) return ascendente ? diferencia : -diferencia
    }

    return b.puntajeTotal - a.puntajeTotal || a.cotizacionId.localeCompare(b.cotizacionId)
  })
}

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
