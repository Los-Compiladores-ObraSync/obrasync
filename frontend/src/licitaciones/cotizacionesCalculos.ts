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
