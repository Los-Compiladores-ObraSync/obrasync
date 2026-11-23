import { describe, expect, it } from 'vitest'
import { diaHistorial, esDiaReal, fechaVisibleHistorial, instanteHistorial } from './fechas'

describe('fechas del historial de Compras', () => {
  it('usa el día de Buenos Aires para timestamps con Z y offsets', () => {
    expect(diaHistorial('2026-11-27T02:00:00Z')).toBe('2026-11-26')
    expect(diaHistorial('2026-11-27T02:00:00+00:00')).toBe('2026-11-26')
    expect(diaHistorial('2026-11-27T00:30:00-03:00')).toBe('2026-11-27')
  })

  it('interpreta valores sin zona como hora de Buenos Aires y fechas sin hora como día calendario', () => {
    expect(diaHistorial('2026-11-27T00:30:00')).toBe('2026-11-27')
    expect(diaHistorial('2026-11-27')).toBe('2026-11-27')
    expect(fechaVisibleHistorial('2026-11-27T02:00:00Z')).toBe('26 de noviembre de 2026')
  })

  it('rechaza días imposibles y no los usa para ordenar', () => {
    expect(esDiaReal('2026-02-31')).toBe(false)
    expect(diaHistorial('2026-02-31T12:00:00Z')).toBeNull()
    expect(fechaVisibleHistorial('2026-02-31')).toBe('Fecha no disponible')
    expect(instanteHistorial('2026-02-31T12:00:00Z')).toBe(Number.NEGATIVE_INFINITY)
  })
})
