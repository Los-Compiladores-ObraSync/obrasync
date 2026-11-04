/**
 * Reglas de validación para el formulario de cotización de proveedores.
 * - precioUnitario > 0 por cada ítem (acepta coma o punto decimal)
 * - flete >= 0 (acepta coma o punto decimal)
 * - plazoEntregaDias: número entero > 0
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
  return undefined
}

export interface FormularioCotizacion {
  flete: string
  plazoEntregaDias: string
  preciosPorArticulo: Record<string, string>
}

export interface ErroresCotizacion {
  flete?: string
  plazoEntregaDias?: string
  preciosPorArticulo?: Record<string, string | undefined>
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
  return false
}
