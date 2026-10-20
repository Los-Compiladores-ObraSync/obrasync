import { describe, expect, it } from 'vitest'
import { fechaEnPalabras } from './fechaEnPalabras'

describe('fechaEnPalabras', () => {
  it('escribe la fecha con el día de la semana', () => {
    expect(fechaEnPalabras('2026-10-26')).toBe('lunes, 26 de octubre de 2026')
  })

  it('devuelve vacío si el texto no es una fecha', () => {
    expect(fechaEnPalabras('')).toBe('')
    expect(fechaEnPalabras('2026-02-31')).toBe('')
    expect(fechaEnPalabras('26/10/2026')).toBe('')
  })
})
