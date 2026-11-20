import type { CriteriosScoringDto } from './licitacionesTipos'

/** Los cuatro criterios del puntaje, en el orden en que se muestran en pantalla. */
export const CRITERIOS_SCORING = [
  { clave: 'precio', etiqueta: 'Precio' },
  { clave: 'flete', etiqueta: 'Flete' },
  { clave: 'plazo', etiqueta: 'Plazo de entrega' },
  { clave: 'calificacion', etiqueta: 'Calificación del proveedor' },
] as const

export type ClaveCriterio = (typeof CRITERIOS_SCORING)[number]['clave']

/** Importancia de cada criterio en estrellas enteras, de 0 (no cuenta) a 5 (el que más pesa). */
export type EstrellasPesos = Record<ClaveCriterio, number>

/**
 * Pasa los pesos del servicio (0 a 1, ej.: 0.5 / 0.2 / 0.2 / 0.1) a estrellas: el criterio que más pesa
 * queda en 5 y los demás en proporción (ej.: 5 / 2 / 2 / 1). Es la misma proporción, así que
 * volver a mandar esas estrellas da el mismo resultado.
 */
export function estrellasDePesos(ponderaciones: CriteriosScoringDto): EstrellasPesos {
  const mayor = Math.max(ponderaciones.precio, ponderaciones.flete, ponderaciones.plazo, ponderaciones.calificacion)
  const aEstrellas = (peso: number) => (mayor > 0 ? Math.round((peso / mayor) * 5) : 0)
  return {
    precio: aEstrellas(ponderaciones.precio),
    flete: aEstrellas(ponderaciones.flete),
    plazo: aEstrellas(ponderaciones.plazo),
    calificacion: aEstrellas(ponderaciones.calificacion),
  }
}

export type ResultadoPesos =
  | { ok: true; pesos: CriteriosScoringDto }
  | { ok: false; mensaje: string }

/**
 * Pasa las estrellas elegidas a los pesos que se mandan al servicio. No hace falta que sumen nada en
 * particular: el servicio los normaliza. Solo se exige que al menos un criterio tenga alguna estrella.
 */
export function pesosDeEstrellas(estrellas: EstrellasPesos): ResultadoPesos {
  const pesos: CriteriosScoringDto = {
    precio: estrellas.precio,
    flete: estrellas.flete,
    plazo: estrellas.plazo,
    calificacion: estrellas.calificacion,
  }
  if (pesos.precio + pesos.flete + pesos.plazo + pesos.calificacion <= 0) {
    return { ok: false, mensaje: 'Al menos un criterio debe tener una estrella.' }
  }
  return { ok: true, pesos }
}
