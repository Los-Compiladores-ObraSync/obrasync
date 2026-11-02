import { describe, expect, it } from 'vitest'
import { fechaLarga } from './formatoFecha'

describe('fechaLarga', () => {
  it('escribe la fecha con palabras', () => {
    // Mediodía UTC: cae en el mismo día en cualquier zona horaria razonable.
    expect(fechaLarga('2026-10-19T12:00:00Z')).toBe('19 de octubre de 2026')
  })

  it('devuelve vacío si no hay fecha o no es válida', () => {
    expect(fechaLarga(null)).toBe('')
    expect(fechaLarga(undefined)).toBe('')
    expect(fechaLarga('')).toBe('')
    expect(fechaLarga('no-es-una-fecha')).toBe('')
  })
})
