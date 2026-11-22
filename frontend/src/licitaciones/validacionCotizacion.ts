/**
 * Reglas de validación para el formulario de cotización de proveedores.
 * - precioUnitario > 0 por cada ítem (acepta coma o punto decimal)
 * - flete >= 0 (acepta coma o punto decimal)
 * - plazoEntregaDias: número entero > 0
 * - otra presentación (opcional por ítem, #229): texto de 1 a 100 caracteres y cantidad >= 0,01
 */

/**
 * Parsea un número en texto aceptando coma o punto como separador decimal.
 * Devuelve el número si es finito o null si no es válido o está vacío.
 */
export function parsearNumeroDecimal(valor: string): number | null {
  const limpio = valor.trim()
  if (!limpio) return null
  const normalizado = limpio.replace(',', '.')
  if (!/^-?\d+(\.\d+)?$/.test(normalizado)) return null
  const num = Number(normalizado)
  return Number.isFinite(num) ? num : null
}

export function validarPrecioUnitario(valor: string): string | undefined {
  const limpio = valor.trim()
  if (!limpio) {
    return 'Ingresá el precio unitario.'
  }
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
  const num = parsearNumeroDecimal(limpio)
  if (num === null) {
    return 'El flete debe ser un número válido.'
  }
  if (num < 0) {
    return 'El flete no puede ser negativo.'
  }
  return undefined
}

/** Tope razonable para que un error de tipeo (22222) no llegue a Compras. Decisión de pantalla, no del contrato. */
export const PLAZO_ENTREGA_MAXIMO_DIAS = 365

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
    return `El plazo de entrega no puede superar los ${PLAZO_ENTREGA_MAXIMO_DIAS} días. Revisá que sean días y no otra unidad.`
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

export function validarCantidadOfrecida(valor: string): string | undefined {
  const limpio = valor.trim()
  if (!limpio) {
    return 'Ingresá la cantidad que ofrecés.'
  }
  const num = parsearNumeroDecimal(limpio)
  if (num === null) {
    return 'La cantidad ofrecida debe ser un número válido.'
  }
  if (num < CANTIDAD_OFRECIDA_MINIMA) {
    return 'La cantidad ofrecida debe ser al menos 0,01.'
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

export function validarCotizacion(
  form: FormularioCotizacion,
  articulosEsperados?: string[],
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
    const errorCantidad = validarCantidadOfrecida(otra.cantidad)
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
