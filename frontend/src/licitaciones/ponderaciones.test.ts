import { describe, expect, it } from 'vitest'
import { pesosParaCampos, validarPesos } from './ponderaciones'

describe('pesosParaCampos', () => {
  it('pasa los pesos de 0 a 1 a porcentajes con coma', () => {
    expect(pesosParaCampos({ precio: 0.5, flete: 0.2, plazo: 0.2, calificacion: 0.1 })).toEqual({
      precio: '50',
      flete: '20',
      plazo: '20',
      calificacion: '10',
    })
  })

  it('no arrastra errores de redondeo ni muestra más de dos decimales', () => {
    const campos = pesosParaCampos({ precio: 0.1 + 0.2, flete: 1 / 3, plazo: 0, calificacion: 0.125 })
    expect(campos.precio).toBe('30')
    expect(campos.flete).toBe('33,33')
    expect(campos.plazo).toBe('0')
    expect(campos.calificacion).toBe('12,5')
  })
})

describe('validarPesos', () => {
  const validos = { precio: '50', flete: '20', plazo: '20', calificacion: '10' }

  it('acepta pesos válidos y los devuelve como números', () => {
    expect(validarPesos(validos)).toEqual({
      ok: true,
      pesos: { precio: 50, flete: 20, plazo: 20, calificacion: 10 },
    })
  })

  it('no hace falta que sumen 100', () => {
    expect(validarPesos({ precio: '1', flete: '1', plazo: '1', calificacion: '1' }).ok).toBe(true)
  })

  it('acepta coma o punto como separador decimal y espacios alrededor', () => {
    expect(validarPesos({ ...validos, precio: ' 12,5 ', flete: '7.5' })).toEqual({
      ok: true,
      pesos: { precio: 12.5, flete: 7.5, plazo: 20, calificacion: 10 },
    })
  })

  it('acepta un criterio en 0 si otro pesa más que 0', () => {
    expect(validarPesos({ precio: '100', flete: '0', plazo: '0', calificacion: '0' }).ok).toBe(true)
  })

  it('rechaza si todos pesan 0', () => {
    expect(validarPesos({ precio: '0', flete: '0', plazo: '0', calificacion: '0' })).toEqual({
      ok: false,
      mensaje: 'Al menos un criterio tiene que pesar más de 0.',
    })
  })

  it('rechaza un negativo, un texto o un campo vacío, y dice cuál criterio es', () => {
    expect(validarPesos({ ...validos, flete: '-5' })).toEqual({
      ok: false,
      mensaje: 'Flete: escribí un número de 0 o más.',
    })
    expect(validarPesos({ ...validos, plazo: 'abc' })).toEqual({
      ok: false,
      mensaje: 'Plazo de entrega: escribí un número de 0 o más.',
    })
    expect(validarPesos({ ...validos, calificacion: '' })).toEqual({
      ok: false,
      mensaje: 'Calificación del proveedor: escribí un número de 0 o más.',
    })
  })
})