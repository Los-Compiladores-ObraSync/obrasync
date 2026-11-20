import { describe, expect, it } from 'vitest'
import { estrellasDePesos, pesosDeEstrellas } from './ponderaciones'

describe('estrellasDePesos', () => {
  it('los pesos por defecto (50/20/20/10) quedan en 5/2/2/1 estrellas', () => {
    expect(estrellasDePesos({ precio: 0.5, flete: 0.2, plazo: 0.2, calificacion: 0.1 })).toEqual({
      precio: 5,
      flete: 2,
      plazo: 2,
      calificacion: 1,
    })
  })

  it('el criterio que más pesa siempre queda en 5, aunque los pesos no sumen 1', () => {
    expect(estrellasDePesos({ precio: 1, flete: 0, plazo: 0, calificacion: 0 })).toEqual({
      precio: 5,
      flete: 0,
      plazo: 0,
      calificacion: 0,
    })
    expect(estrellasDePesos({ precio: 0.25, flete: 0.25, plazo: 0.25, calificacion: 0.25 })).toEqual({
      precio: 5,
      flete: 5,
      plazo: 5,
      calificacion: 5,
    })
  })

  it('si todo pesa 0 no divide por cero', () => {
    expect(estrellasDePesos({ precio: 0, flete: 0, plazo: 0, calificacion: 0 })).toEqual({
      precio: 0,
      flete: 0,
      plazo: 0,
      calificacion: 0,
    })
  })
})

describe('pesosDeEstrellas', () => {
  it('manda las estrellas como pesos (el servicio las normaliza)', () => {
    expect(pesosDeEstrellas({ precio: 5, flete: 2, plazo: 2, calificacion: 1 })).toEqual({
      ok: true,
      pesos: { precio: 5, flete: 2, plazo: 2, calificacion: 1 },
    })
  })

  it('acepta un solo criterio con estrellas', () => {
    expect(pesosDeEstrellas({ precio: 3, flete: 0, plazo: 0, calificacion: 0 }).ok).toBe(true)
  })

  it('rechaza si todos tienen 0 estrellas', () => {
    expect(pesosDeEstrellas({ precio: 0, flete: 0, plazo: 0, calificacion: 0 })).toEqual({
      ok: false,
      mensaje: 'Al menos un criterio debe tener una estrella.',
    })
  })
})
