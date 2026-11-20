import { describe, expect, it } from 'vitest'
import { compararConPrecioPrevio } from './cotizacionesCalculos'

describe('compararConPrecioPrevio', () => {
  it('sin compra anterior no hay comparación', () => {
    expect(compararConPrecioPrevio(100, undefined)).toBeNull()
  })

  it('más caro que la última compra', () => {
    const r = compararConPrecioPrevio(110, { ultimoPrecioUnitario: 100 })
    expect(r?.texto).toBe('10 % más caro que la última compra')
    expect(r?.porcentaje).toBeCloseTo(10)
  })

  it('más barato que la última compra', () => {
    expect(compararConPrecioPrevio(90, { ultimoPrecioUnitario: 100 })?.texto).toBe('10 % más barato que la última compra')
  })

  it('una diferencia menor a 0,5 % se considera igual', () => {
    expect(compararConPrecioPrevio(100.3, { ultimoPrecioUnitario: 100 })?.texto).toBe('igual que la última compra')
  })

  it('un precio previo en cero no se usa (evita dividir por cero)', () => {
    expect(compararConPrecioPrevio(50, { ultimoPrecioUnitario: 0 })).toBeNull()
  })
})
