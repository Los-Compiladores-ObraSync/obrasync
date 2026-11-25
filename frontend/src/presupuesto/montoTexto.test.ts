import { describe, expect, it } from 'vitest'
import { parsearMonto, parsearPrecioDeArchivo } from './montoTexto'

describe('parsearMonto', () => {
  it('acepta pesos enteros con el punto como separador de miles', () => {
    expect(parsearMonto('1.500.000')).toBe(1500000)
    expect(parsearMonto('10.000')).toBe(10000)
  })

  it('acepta números sin separador de miles', () => {
    expect(parsearMonto('1500000')).toBe(1500000)
    expect(parsearMonto('0')).toBe(0)
  })

  it('acepta espacios y el símbolo $', () => {
    expect(parsearMonto('  $ 2.500 ')).toBe(2500)
  })

  it('NUNCA acepta coma ni decimales', () => {
    expect(parsearMonto('1500000,5')).toBeNull()
    expect(parsearMonto('2.500,00')).toBeNull()
    expect(parsearMonto('1500,50')).toBeNull()
    expect(parsearMonto('1500.5')).toBeNull()
  })

  it('rechaza vacío, negativos y letras', () => {
    expect(parsearMonto('')).toBeNull()
    expect(parsearMonto('   ')).toBeNull()
    expect(parsearMonto('-100')).toBeNull()
    expect(parsearMonto('abc')).toBeNull()
  })

  it('rechaza miles mal puestos en vez de adivinar', () => {
    expect(parsearMonto('1.50')).toBeNull()
    expect(parsearMonto('1.5000')).toBeNull()
  })
})

describe('parsearPrecioDeArchivo (CSV de carga masiva)', () => {
  it('acepta el formato argentino que exporta Excel: miles con punto y decimales con coma', () => {
    expect(parsearPrecioDeArchivo('1.500.000,50')).toBe(1500000.5)
    expect(parsearPrecioDeArchivo('10.000')).toBe(10000)
    expect(parsearPrecioDeArchivo('1500000,5')).toBe(1500000.5)
    expect(parsearPrecioDeArchivo('  $ 2.500,00 ')).toBe(2500)
  })

  it('rechaza vacío, negativos, letras y más de 2 decimales', () => {
    for (const malo of ['', '   ', '-100', 'abc', '10,123', '1.50', '1.5000']) {
      expect(parsearPrecioDeArchivo(malo)).toBeNull()
    }
  })
})
