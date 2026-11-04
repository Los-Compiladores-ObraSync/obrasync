import { describe, expect, it } from 'vitest'
import {
  hayErroresCotizacion,
  parsearNumeroDecimal,
  validarCotizacion,
  validarFlete,
  validarPlazoEntregaDias,
  validarPrecioUnitario,
} from './validacionCotizacion'

describe('parsearNumeroDecimal', () => {
  it('parsea números con punto y con coma', () => {
    expect(parsearNumeroDecimal('100.5')).toBe(100.5)
    expect(parsearNumeroDecimal('100,5')).toBe(100.5)
    expect(parsearNumeroDecimal('  42  ')).toBe(42)
    expect(parsearNumeroDecimal('0')).toBe(0)
    expect(parsearNumeroDecimal('0,0')).toBe(0)
  })

  it('devuelve null si no es un número válido o está vacío', () => {
    expect(parsearNumeroDecimal('')).toBeNull()
    expect(parsearNumeroDecimal('   ')).toBeNull()
    expect(parsearNumeroDecimal('abc')).toBeNull()
    expect(parsearNumeroDecimal('12.34.56')).toBeNull()
    expect(parsearNumeroDecimal('12,34,56')).toBeNull()
  })
})

describe('validarPrecioUnitario', () => {
  it('acepta precios positivos con coma o punto', () => {
    expect(validarPrecioUnitario('1500')).toBeUndefined()
    expect(validarPrecioUnitario('1500.50')).toBeUndefined()
    expect(validarPrecioUnitario('1500,50')).toBeUndefined()
  })

  it('rechaza campo vacío o espacios', () => {
    expect(validarPrecioUnitario('')).toBe('Ingresá el precio unitario.')
    expect(validarPrecioUnitario('  ')).toBe('Ingresá el precio unitario.')
  })

  it('rechaza precios menores o iguales a cero', () => {
    expect(validarPrecioUnitario('0')).toBe('El precio unitario debe ser mayor a 0.')
    expect(validarPrecioUnitario('0,00')).toBe('El precio unitario debe ser mayor a 0.')
    expect(validarPrecioUnitario('-10')).toBe('El precio unitario debe ser mayor a 0.')
    expect(validarPrecioUnitario('-10,50')).toBe('El precio unitario debe ser mayor a 0.')
  })

  it('rechaza textos que no sean numéricos', () => {
    expect(validarPrecioUnitario('cien')).toBe('El precio unitario debe ser un número válido.')
  })
})

describe('validarFlete', () => {
  it('acepta flete cero o positivo con coma o punto', () => {
    expect(validarFlete('0')).toBeUndefined()
    expect(validarFlete('0,00')).toBeUndefined()
    expect(validarFlete('2500')).toBeUndefined()
    expect(validarFlete('2500,50')).toBeUndefined()
    expect(validarFlete('2500.50')).toBeUndefined()
  })

  it('rechaza flete vacío', () => {
    expect(validarFlete('')).toBe('Ingresá el costo de flete.')
    expect(validarFlete('   ')).toBe('Ingresá el costo de flete.')
  })

  it('rechaza flete negativo', () => {
    expect(validarFlete('-50')).toBe('El flete no puede ser negativo.')
    expect(validarFlete('-0.1')).toBe('El flete no puede ser negativo.')
  })

  it('rechaza formato no numérico', () => {
    expect(validarFlete('gratis')).toBe('El flete debe ser un número válido.')
  })
})

describe('validarPlazoEntregaDias', () => {
  it('acepta enteros positivos', () => {
    expect(validarPlazoEntregaDias('1')).toBeUndefined()
    expect(validarPlazoEntregaDias('15')).toBeUndefined()
    expect(validarPlazoEntregaDias('30')).toBeUndefined()
  })

  it('rechaza plazo vacío', () => {
    expect(validarPlazoEntregaDias('')).toBe('Ingresá el plazo de entrega.')
  })

  it('rechaza decimales con coma o punto', () => {
    expect(validarPlazoEntregaDias('2.5')).toBe(
      'El plazo de entrega debe ser un número entero de días.',
    )
    expect(validarPlazoEntregaDias('2,5')).toBe(
      'El plazo de entrega debe ser un número entero de días.',
    )
  })

  it('rechaza valores menores o iguales a cero', () => {
    expect(validarPlazoEntregaDias('0')).toBe('El plazo de entrega debe ser mayor a 0.')
    expect(validarPlazoEntregaDias('-5')).toBe('El plazo de entrega debe ser mayor a 0.')
  })

  it('rechaza texto no numérico', () => {
    expect(validarPlazoEntregaDias('dos semanas')).toBe(
      'El plazo de entrega debe ser un número entero.',
    )
  })
})

describe('validarCotizacion y hayErroresCotizacion', () => {
  const art1 = 'art-1'
  const art2 = 'art-2'

  it('valida formulario correcto completo', () => {
    const errores = validarCotizacion(
      {
        flete: '1000',
        plazoEntregaDias: '7',
        preciosPorArticulo: {
          [art1]: '500,25',
          [art2]: '350',
        },
      },
      [art1, art2],
    )

    expect(hayErroresCotizacion(errores)).toBe(false)
    expect(errores).toEqual({})
  })

  it('detecta errores individuales en flete, plazo e ítems', () => {
    const errores = validarCotizacion(
      {
        flete: '-1',
        plazoEntregaDias: '0',
        preciosPorArticulo: {
          [art1]: '0',
          [art2]: '',
        },
      },
      [art1, art2],
    )

    expect(hayErroresCotizacion(errores)).toBe(true)
    expect(errores.flete).toBe('El flete no puede ser negativo.')
    expect(errores.plazoEntregaDias).toBe('El plazo de entrega debe ser mayor a 0.')
    expect(errores.preciosPorArticulo?.[art1]).toBe('El precio unitario debe ser mayor a 0.')
    expect(errores.preciosPorArticulo?.[art2]).toBe('Ingresá el precio unitario.')
  })

  it('detecta si no hay ítems esperados', () => {
    const errores = validarCotizacion({
      flete: '0',
      plazoEntregaDias: '5',
      preciosPorArticulo: {},
    }, [])

    expect(hayErroresCotizacion(errores)).toBe(true)
    expect(errores.general).toBe('La cotización debe incluir al menos un ítem.')
  })
})
