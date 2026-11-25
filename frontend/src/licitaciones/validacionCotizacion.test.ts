import { describe, expect, it } from 'vitest'
import {
  hayErroresCotizacion,
  parsearNumeroDecimal,
  validarCantidadOfrecida,
  validarCotizacion,
  validarFlete,
  validarPresentacionOfrecida,
  validarPlazoEntregaDias,
  validarPrecioUnitario,
} from './validacionCotizacion'

describe('parsearNumeroDecimal', () => {
  it('parsea números con punto', () => {
    expect(parsearNumeroDecimal('100.5')).toBe(100.5)
    expect(parsearNumeroDecimal('  42  ')).toBe(42)
    expect(parsearNumeroDecimal('0')).toBe(0)
    expect(parsearNumeroDecimal('0.0')).toBe(0)
  })

  it('NO acepta la coma como separador decimal', () => {
    expect(parsearNumeroDecimal('100,5')).toBeNull()
    expect(parsearNumeroDecimal('0,0')).toBeNull()
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
  it('acepta precios positivos con punto', () => {
    expect(validarPrecioUnitario('1500')).toBeUndefined()
    expect(validarPrecioUnitario('1500.50')).toBeUndefined()
  })

  it('rechaza la coma: hay que usar punto', () => {
    expect(validarPrecioUnitario('1500,50')).toBe('Usá punto en lugar de coma para los decimales.')
  })

  it('rechaza campo vacío o espacios', () => {
    expect(validarPrecioUnitario('')).toBe('Ingresá el precio unitario.')
    expect(validarPrecioUnitario('  ')).toBe('Ingresá el precio unitario.')
  })

  it('rechaza precios menores o iguales a cero', () => {
    expect(validarPrecioUnitario('0')).toBe('El precio unitario debe ser mayor a 0.')
    expect(validarPrecioUnitario('0.00')).toBe('El precio unitario debe ser mayor a 0.')
    expect(validarPrecioUnitario('-10')).toBe('El precio unitario debe ser mayor a 0.')
    expect(validarPrecioUnitario('-10.50')).toBe('El precio unitario debe ser mayor a 0.')
  })

  it('rechaza textos que no sean numéricos', () => {
    expect(validarPrecioUnitario('cien')).toBe('El precio unitario debe ser un número válido.')
  })
})

describe('validarFlete', () => {
  it('acepta flete cero o positivo con punto', () => {
    expect(validarFlete('0')).toBeUndefined()
    expect(validarFlete('0.00')).toBeUndefined()
    expect(validarFlete('2500')).toBeUndefined()
    expect(validarFlete('2500.50')).toBeUndefined()
  })

  it('rechaza la coma: hay que usar punto', () => {
    expect(validarFlete('2500,50')).toBe('Usá punto en lugar de coma para los decimales.')
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
  it('acepta enteros de 1 a 15 días', () => {
    expect(validarPlazoEntregaDias('1')).toBeUndefined()
    expect(validarPlazoEntregaDias('7')).toBeUndefined()
    expect(validarPlazoEntregaDias('15')).toBeUndefined()
  })

  it('rechaza más de 15 días: no tiene sentido ofrecer un año', () => {
    const mensaje = 'El plazo de entrega no puede superar los 15 días, contados desde que cotizás.'
    expect(validarPlazoEntregaDias('16')).toBe(mensaje)
    expect(validarPlazoEntregaDias('30')).toBe(mensaje)
    expect(validarPlazoEntregaDias('365')).toBe(mensaje)
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
          [art1]: '500.25',
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


describe('validarPresentacionOfrecida (#229)', () => {
  it('acepta un texto de hasta 100 caracteres', () => {
    expect(validarPresentacionOfrecida('bolsa de 25 kg')).toBeUndefined()
    expect(validarPresentacionOfrecida('a'.repeat(100))).toBeUndefined()
  })

  it('pide la presentación si está vacía o son solo espacios', () => {
    expect(validarPresentacionOfrecida('')).toBe('Escribí qué presentación ofrecés (por ejemplo, bolsa de 25 kg).')
    expect(validarPresentacionOfrecida('   ')).toBe('Escribí qué presentación ofrecés (por ejemplo, bolsa de 25 kg).')
  })

  it('rechaza más de 100 caracteres (límite del contrato)', () => {
    expect(validarPresentacionOfrecida('a'.repeat(101))).toBe('La presentación no puede superar los 100 caracteres.')
  })
})

describe('validarCantidadOfrecida (#229)', () => {
  it('acepta cantidades con punto desde 0.01 si no se cuentan en bolsas', () => {
    expect(validarCantidadOfrecida('2')).toBeUndefined()
    expect(validarCantidadOfrecida('2.5')).toBeUndefined()
    expect(validarCantidadOfrecida('0.01')).toBeUndefined()
  })

  it('rechaza la coma: hay que usar punto', () => {
    expect(validarCantidadOfrecida('2,5')).toBe('Usá punto en lugar de coma para los decimales.')
  })

  it('si se cuenta en bolsas, solo acepta enteros', () => {
    expect(validarCantidadOfrecida('2', true)).toBeUndefined()
    expect(validarCantidadOfrecida('1.4', true)).toBe('Las bolsas se cuentan enteras: escribí un número sin decimales.')
    expect(validarCantidadOfrecida('1,4', true)).toBe('Usá punto en lugar de coma para los decimales.')
  })

  it('rechaza vacío, texto, cero y negativos', () => {
    expect(validarCantidadOfrecida('')).toBe('Ingresá la cantidad que ofrecés.')
    expect(validarCantidadOfrecida('abc')).toBe('La cantidad ofrecida debe ser un número válido.')
    expect(validarCantidadOfrecida('0')).toBe('La cantidad ofrecida debe ser al menos 0.01.')
    expect(validarCantidadOfrecida('0.005')).toBe('La cantidad ofrecida debe ser al menos 0.01.')
    expect(validarCantidadOfrecida('-3')).toBe('La cantidad ofrecida debe ser al menos 0.01.')
  })
})

describe('validarCotizacion con otra presentación (#229)', () => {
  const art1 = 'aaaaaaaa-0000-0000-0000-000000000001'
  const art2 = 'aaaaaaaa-0000-0000-0000-000000000002'
  const base = { flete: '0', plazoEntregaDias: '5', preciosPorArticulo: { [art1]: '100', [art2]: '50' } }

  it('sin otra presentación se comporta como siempre (no agrega errores)', () => {
    const errores = validarCotizacion(base, [art1, art2])
    expect(hayErroresCotizacion(errores)).toBe(false)
    expect(errores.otrasPresentacionesPorArticulo).toBeUndefined()
  })

  it('acepta una otra presentación completa y válida', () => {
    const errores = validarCotizacion(
      { ...base, otrasPresentacionesPorArticulo: { [art1]: { presentacion: 'bolsa 25 kg', cantidad: '2' } } },
      [art1, art2],
    )
    expect(hayErroresCotizacion(errores)).toBe(false)
  })

  it('marca los campos de la presentación activada que están mal, sin tocar los otros ítems', () => {
    const errores = validarCotizacion(
      { ...base, otrasPresentacionesPorArticulo: { [art1]: { presentacion: '', cantidad: '0' } } },
      [art1, art2],
    )
    expect(hayErroresCotizacion(errores)).toBe(true)
    expect(errores.otrasPresentacionesPorArticulo?.[art1]?.presentacion).toBeDefined()
    expect(errores.otrasPresentacionesPorArticulo?.[art1]?.cantidad).toBe('La cantidad ofrecida debe ser al menos 0.01.')
    expect(errores.otrasPresentacionesPorArticulo?.[art2]).toBeUndefined()
  })

  it('una presentación en bolsas no admite decimales, y se deduce del material pedido o del texto', () => {
    const conBolsas = validarCotizacion(
      { ...base, otrasPresentacionesPorArticulo: { [art1]: { presentacion: 'Bolsa de 25 kg', cantidad: '1.4' } } },
      [art1, art2],
      { [art1]: 'KG', [art2]: 'M3' },
    )
    expect(conBolsas.otrasPresentacionesPorArticulo?.[art1]?.cantidad).toBe('Las bolsas se cuentan enteras: escribí un número sin decimales.')

    const materialEnBolsas = validarCotizacion(
      { ...base, otrasPresentacionesPorArticulo: { [art1]: { presentacion: 'pack de 2', cantidad: '1.4' } } },
      [art1, art2],
      { [art1]: 'BOLSA', [art2]: 'M3' },
    )
    expect(materialEnBolsas.otrasPresentacionesPorArticulo?.[art1]?.cantidad).toBe('Las bolsas se cuentan enteras: escribí un número sin decimales.')

    const sinBolsas = validarCotizacion(
      { ...base, otrasPresentacionesPorArticulo: { [art1]: { presentacion: 'balde de 20 L', cantidad: '1.4' } } },
      [art1, art2],
      { [art1]: 'M3', [art2]: 'M3' },
    )
    expect(hayErroresCotizacion(sinBolsas)).toBe(false)
  })

  it('el precio sigue siendo obligatorio aunque la presentación esté bien', () => {
    const errores = validarCotizacion(
      {
        ...base,
        preciosPorArticulo: { [art1]: '', [art2]: '50' },
        otrasPresentacionesPorArticulo: { [art1]: { presentacion: 'bolsa 25 kg', cantidad: '2' } },
      },
      [art1, art2],
    )
    expect(hayErroresCotizacion(errores)).toBe(true)
    expect(errores.preciosPorArticulo?.[art1]).toBe('Ingresá el precio unitario.')
  })
})
