import { describe, expect, it } from 'vitest'
import { formatearPlazoEntrega, formatearPlazoOInmediato } from './plazoEntrega'

describe('formatearPlazoEntrega', () => {
  it('usa el singular para 1 día', () => {
    expect(formatearPlazoEntrega(1)).toBe('1 día')
  })

  it('usa el plural para más de un día', () => {
    expect(formatearPlazoEntrega(5)).toBe('5 días')
    expect(formatearPlazoEntrega(30)).toBe('30 días')
  })
})

describe('formatearPlazoOInmediato', () => {
  it('muestra "Inmediato" si no hay plazo o es 0', () => {
    expect(formatearPlazoOInmediato(undefined)).toBe('Inmediato')
    expect(formatearPlazoOInmediato(null)).toBe('Inmediato')
    expect(formatearPlazoOInmediato(0)).toBe('Inmediato')
  })

  it('muestra el plazo con su unidad si viene informado', () => {
    expect(formatearPlazoOInmediato(1)).toBe('1 día')
    expect(formatearPlazoOInmediato(10)).toBe('10 días')
  })
})
