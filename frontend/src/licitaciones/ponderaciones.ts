import type { CriteriosScoringDto } from './licitacionesTipos'

/** Los cuatro criterios del puntaje, en el orden en que se muestran en pantalla. */
export const CRITERIOS_SCORING = [
  { clave: 'precio', etiqueta: 'Precio' },
  { clave: 'flete', etiqueta: 'Flete' },
  { clave: 'plazo', etiqueta: 'Plazo de entrega' },
  { clave: 'calificacion', etiqueta: 'Calificación del proveedor' },
] as const

export type ClaveCriterio = (typeof CRITERIOS_SCORING)[number]['clave']

/** Lo que escribió la persona en cada campo (texto, porque puede estar a medio escribir). */
export type PesosEscritos = Record<ClaveCriterio, string>

/**
 * Pasa los pesos del servicio (0 a 1, ej.: 0.5) a lo que se muestra en los campos (porcentaje, ej.: "50").
 * Hasta dos decimales y con coma, como se escribe en Argentina.
 */
export function pesosParaCampos(ponderaciones: CriteriosScoringDto): PesosEscritos {
  const comoTexto = (peso: number) =>
    String(Math.round(peso * 100 * 100) / 100).replace('.', ',')
  return {
    precio: comoTexto(ponderaciones.precio),
    flete: comoTexto(ponderaciones.flete),
    plazo: comoTexto(ponderaciones.plazo),
    calificacion: comoTexto(ponderaciones.calificacion),
  }
}

export type ResultadoPesos =
  | { ok: true; pesos: CriteriosScoringDto }
  | { ok: false; mensaje: string }

/**
 * Revisa lo escrito antes de pedirle al servicio que recalcule (la misma regla del contrato):
 * cada peso es un número de 0 o más y la suma es mayor que 0. No hace falta que sumen 100.
 * Acepta coma o punto como separador decimal.
 */
export function validarPesos(escritos: PesosEscritos): ResultadoPesos {
  const pesos = {} as CriteriosScoringDto
  for (const { clave, etiqueta } of CRITERIOS_SCORING) {
    const texto = escritos[clave].trim().replace(',', '.')
    // Number('') es 0: un campo vacío no es un 0 escrito a propósito, es un olvido.
    const numero = texto === '' ? Number.NaN : Number(texto)
    if (!Number.isFinite(numero) || numero < 0) {
      return { ok: false, mensaje: `${etiqueta}: escribí un número de 0 o más.` }
    }
    pesos[clave] = numero
  }
  if (pesos.precio + pesos.flete + pesos.plazo + pesos.calificacion <= 0) {
    return { ok: false, mensaje: 'Al menos un criterio tiene que pesar más de 0.' }
  }
  return { ok: true, pesos }
}