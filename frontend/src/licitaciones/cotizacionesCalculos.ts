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

/**
 * Formatea un número como importe con "$", sin código de moneda ARS.
 * Ej: 850000 -> "$ 850.000"
 */
export function formatoPrecio(valor: number): string {
  return `$ ${valor.toLocaleString('es-AR')}`
}
