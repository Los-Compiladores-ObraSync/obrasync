import { describe, expect, it } from 'vitest'
import { esFechaFutura, esFechaPasada, fechaEnPalabras, hoyComoTexto } from './fechaEnPalabras'

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

describe('esFechaPasada', () => {
  // Martes 3 de noviembre de 2026, a la noche (hora local).
  const ahora = new Date(2026, 10, 3, 22, 30)

  it('una fecha anterior a hoy es pasada', () => {
    expect(esFechaPasada('2026-11-02', ahora)).toBe(true)
    expect(esFechaPasada('2020-01-01', ahora)).toBe(true)
  })

  it('hoy y las fechas futuras no son pasadas', () => {
    expect(esFechaPasada('2026-11-03', ahora)).toBe(false)
    expect(esFechaPasada('2026-11-04', ahora)).toBe(false)
  })

  it('si el texto no es una fecha, no avisa', () => {
    expect(esFechaPasada('', ahora)).toBe(false)
    expect(esFechaPasada('2026-02-31', ahora)).toBe(false)
  })
})

describe('esFechaFutura', () => {
  // Martes 3 de noviembre de 2026, a la noche (hora local): en UTC ya es el 4.
  const ahora = new Date(2026, 10, 3, 22, 30)

  it('una fecha posterior a hoy es futura', () => {
    expect(esFechaFutura('2026-11-04', ahora)).toBe(true)
  })

  it('hoy y las fechas pasadas no son futuras', () => {
    expect(esFechaFutura('2026-11-03', ahora)).toBe(false)
    expect(esFechaFutura('2026-11-02', ahora)).toBe(false)
  })

  it('si el texto no es una fecha, no es futura', () => {
    expect(esFechaFutura('', ahora)).toBe(false)
  })
})

describe('hoyComoTexto', () => {
  it('da el día local con ceros a la izquierda', () => {
    expect(hoyComoTexto(new Date(2026, 0, 5, 9, 0))).toBe('2026-01-05')
  })

  it('de noche sigue siendo el día local, no el de UTC', () => {
    expect(hoyComoTexto(new Date(2026, 10, 3, 22, 30))).toBe('2026-11-03')
  })
})
