import { describe, expect, it } from 'vitest'
import { parsearMonto } from './montoTexto'

describe('parsearMonto', () => {
  it('acepta el formato argentino con miles y decimales', () => {
    expect(parsearMonto('1.500.000,50')).toBe(1500000.5)
    expect(parsearMonto('10.000')).toBe(10000)
  })

  it('acepta números sin separador de miles', () => {
    expect(parsearMonto('1500000')).toBe(1500000)
    expect(parsearMonto('1500000,5')).toBe(1500000.5)
    expect(parsearMonto('0')).toBe(0)
  })

  it('acepta espacios y el símbolo $', () => {
    expect(parsearMonto('  $ 2.500,00 ')).toBe(2500)
  })

  it('rechaza vacío, negativos, letras y más de 2 decimales', () => {
    expect(parsearMonto('')).toBeNull()
    expect(parsearMonto('   ')).toBeNull()
    expect(parsearMonto('-100')).toBeNull()
    expect(parsearMonto('abc')).toBeNull()
    expect(parsearMonto('10,123')).toBeNull()
  })

  it('rechaza miles mal puestos en vez de adivinar', () => {
    expect(parsearMonto('1.50')).toBeNull()
    expect(parsearMonto('1.5000')).toBeNull()
  })
})
