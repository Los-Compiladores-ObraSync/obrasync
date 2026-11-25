/**
 * Reglas de validación para el formulario de cotización de proveedores.
 * - precioUnitario > 0 por cada ítem (decimales con punto; la coma no se acepta)
 * - flete >= 0 (decimales con punto; la coma no se acepta)
 * - plazoEntregaDias: número entero entre 1 y 15 (contados desde que se cotiza)
 * - otra presentación (opcional por ítem, #229): texto de 1 a 100 caracteres y cantidad >= 0,01
 */

import { errorDeFormatoNumerico, esPresentacionEntera, MENSAJE_SOLO_ENTEROS } from '../formularios/numeros'

/**
 * Parsea un número en texto con PUNTO como separador decimal. La coma no se acepta ("1,4" no es un número válido).
 * Devuelve el número si es finito o null si no es válido o está vacío.
 */
export function parsearNumeroDecimal(valor: string): number | null {
  const limpio = valor.trim()
  if (!limpio) return null
  if (!/^-?\d+(\.\d+)?$/.test(limpio)) return null
  const num = Number(limpio)
  return Number.isFinite(num) ? num : null
}

export function validarPrecioUnitario(valor: string): string | undefined {
  const limpio = valor.trim()
  if (!limpio) {
    return 'Ingresá el precio unitario.'
  }
  const errorFormato = errorDeFormatoNumerico(limpio)
  if (errorFormato) return errorFormato
  const num = parsearNumeroDecimal(limpio)
  if (num === null) {
    return 'El precio unitario debe ser un número válido.'
  }
  if (num <= 0) {
    return 'El precio unitario debe ser mayor a 0.'
  }
  return undefined
}

export function validarFlete(valor: string): string | undefined {
  const limpio = valor.trim()
  if (!limpio) {
    return 'Ingresá el costo de flete.'
  }
  const errorFormato = errorDeFormatoNumerico(limpio)
  if (errorFormato) return errorFormato
  const num = parsearNumeroDecimal(limpio)
  if (num === null) {
    return 'El flete debe ser un número válido.'
  }
  if (num < 0) {
    return 'El flete no puede ser negativo.'
  }
  return undefined
}

/**
 * Máximo plazo de entrega que puede ofrecer un proveedor, en días corridos contados desde que cotiza.
 * Lo valida igual el servicio de Licitaciones: no tiene sentido ofrecer un año.
 */
export const PLAZO_ENTREGA_MAXIMO_DIAS = 15

export function validarPlazoEntregaDias(valor: string): string | undefined {
  const limpio = valor.trim()
  if (!limpio) {
    return 'Ingresá el plazo de entrega.'
  }
  if (limpio.includes('.') || limpio.includes(',')) {
    return 'El plazo de entrega debe ser un número entero de días.'
  }
  if (!/^-?\d+$/.test(limpio)) {
    return 'El plazo de entrega debe ser un número entero.'
  }
  const num = Number(limpio)
  if (num <= 0) {
    return 'El plazo de entrega debe ser mayor a 0.'
  }
  if (num > PLAZO_ENTREGA_MAXIMO_DIAS) {
    return `El plazo de entrega no puede superar los ${PLAZO_ENTREGA_MAXIMO_DIAS} días, contados desde que cotizás.`
  }
  return undefined
}

/** Límites del contrato (CrearCotizacionItemDto, #190): la pantalla valida lo mismo que el servicio. */
export const PRESENTACION_MAXIMA_CARACTERES = 100
export const CANTIDAD_OFRECIDA_MINIMA = 0.01

export function validarPresentacionOfrecida(valor: string): string | undefined {
  const limpio = valor.trim()
  if (!limpio) {
    return 'Escribí qué presentación ofrecés (por ejemplo, bolsa de 25 kg).'
  }
  if (limpio.length > PRESENTACION_MAXIMA_CARACTERES) {
    return `La presentación no puede superar los ${PRESENTACION_MAXIMA_CARACTERES} caracteres.`
  }
  return undefined
}

/** Si la presentación ofrecida se cuenta en bolsas, la cantidad tiene que ser entera (ver formularios/numeros). */
export function validarCantidadOfrecida(valor: string, entera = false): string | undefined {
  const limpio = valor.trim()
  if (!limpio) {
    return 'Ingresá la cantidad que ofrecés.'
  }
  const errorFormato = errorDeFormatoNumerico(limpio, entera)
  if (errorFormato) return errorFormato
  const num = parsearNumeroDecimal(limpio)
  if (num === null) {
    return 'La cantidad ofrecida debe ser un número válido.'
  }
  if (num < CANTIDAD_OFRECIDA_MINIMA) {
    return 'La cantidad ofrecida debe ser al menos 0.01.'
  }
  if (entera && !Number.isInteger(num)) {
    return MENSAJE_SOLO_ENTEROS
  }
  return undefined
}

/** Lo que carga el Proveedor cuando activa "Ofrezco otra presentación" en un ítem. */
export interface OtraPresentacionFormulario {
  presentacion: string
  cantidad: string
}

export interface ErroresOtraPresentacion {
  presentacion?: string
  cantidad?: string
}

export interface FormularioCotizacion {
  flete: string
  plazoEntregaDias: string
  preciosPorArticulo: Record<string, string>
  /** Solo tiene entrada el ítem donde el Proveedor activó la opción; si no hay entrada, cotiza lo pedido. */
  otrasPresentacionesPorArticulo?: Record<string, OtraPresentacionFormulario | undefined>
}

export interface ErroresCotizacion {
  flete?: string
  plazoEntregaDias?: string
  preciosPorArticulo?: Record<string, string | undefined>
  otrasPresentacionesPorArticulo?: Record<string, ErroresOtraPresentacion | undefined>
  general?: string
}

/**
 * @param unidadesPorArticulo unidad de medida del material pedido (BOLSA, KG…), para saber si lo que se ofrece se cuenta en enteros.
 */
export function validarCotizacion(
  form: FormularioCotizacion,
  articulosEsperados?: string[],
  unidadesPorArticulo?: Record<string, string | undefined>,
): ErroresCotizacion {
  const errores: ErroresCotizacion = {}

  const errorFlete = validarFlete(form.flete)
  if (errorFlete) errores.flete = errorFlete

  const errorPlazo = validarPlazoEntregaDias(form.plazoEntregaDias)
  if (errorPlazo) errores.plazoEntregaDias = errorPlazo

  const erroresItems: Record<string, string | undefined> = {}
  const articulos = articulosEsperados ?? Object.keys(form.preciosPorArticulo)

  if (articulos.length === 0) {
    errores.general = 'La cotización debe incluir al menos un ítem.'
  } else {
    for (const artId of articulos) {
      const valor = form.preciosPorArticulo[artId] ?? ''
      const errorItem = validarPrecioUnitario(valor)
      if (errorItem) {
        erroresItems[artId] = errorItem
      }
    }
  }

  if (Object.keys(erroresItems).length > 0) {
    errores.preciosPorArticulo = erroresItems
  }

  const erroresPresentaciones: Record<string, ErroresOtraPresentacion | undefined> = {}
  for (const artId of articulos) {
    const otra = form.otrasPresentacionesPorArticulo?.[artId]
    if (!otra) continue
    const errorPresentacion = validarPresentacionOfrecida(otra.presentacion)
    const errorCantidad = validarCantidadOfrecida(
      otra.cantidad,
      esPresentacionEntera(otra.presentacion, unidadesPorArticulo?.[artId]),
    )
    if (errorPresentacion || errorCantidad) {
      erroresPresentaciones[artId] = { presentacion: errorPresentacion, cantidad: errorCantidad }
    }
  }
  if (Object.keys(erroresPresentaciones).length > 0) {
    errores.otrasPresentacionesPorArticulo = erroresPresentaciones
  }

  return errores
}

export function hayErroresCotizacion(errores: ErroresCotizacion): boolean {
  if (errores.flete || errores.plazoEntregaDias || errores.general) {
    return true
  }
  if (
    errores.preciosPorArticulo &&
    Object.values(errores.preciosPorArticulo).some(Boolean)
  ) {
    return true
  }
  if (
    errores.otrasPresentacionesPorArticulo &&
    Object.values(errores.otrasPresentacionesPorArticulo).some((e) => e?.presentacion || e?.cantidad)
  ) {
    return true
  }
  return false
}
