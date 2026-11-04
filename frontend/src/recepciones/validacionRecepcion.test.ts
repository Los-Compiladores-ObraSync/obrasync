import { describe, expect, it } from 'vitest'
import { hayErrores, validarRecepcion } from './validacionRecepcion'

// Martes 3 de noviembre de 2026, a la noche (hora local).
const ahora = new Date(2026, 10, 3, 22, 30)
const valido = { numeroRemito: '0001-00012345', fecha: '2026-11-03', observaciones: '' }

describe('validarRecepcion', () => {
  it('con datos correctos no hay errores', () => {
    expect(hayErrores(validarRecepcion(valido, ahora))).toBe(false)
  })

  it('el número de remito es obligatorio y tiene tope de 50 caracteres', () => {
    expect(validarRecepcion({ ...valido, numeroRemito: '   ' }, ahora).numeroRemito).toBe(
      'El número de remito es obligatorio.',
    )
    expect(validarRecepcion({ ...valido, numeroRemito: 'x'.repeat(51) }, ahora).numeroRemito).toMatch(
      /más de 50 caracteres/,
    )
  })

  it('la fecha es obligatoria y tiene que ser válida', () => {
    expect(validarRecepcion({ ...valido, fecha: '' }, ahora).fecha).toBe(
      'La fecha de recepción es obligatoria.',
    )
    expect(validarRecepcion({ ...valido, fecha: '2026-02-31' }, ahora).fecha).toBe(
      'Ingresá una fecha válida.',
    )
  })

  it('la fecha no puede ser futura (#127), pero hoy y las pasadas sí', () => {
    expect(validarRecepcion({ ...valido, fecha: '2026-11-04' }, ahora).fecha).toBe(
      'La fecha de recepción no puede ser futura.',
    )
    expect(validarRecepcion({ ...valido, fecha: '2026-11-03' }, ahora).fecha).toBeUndefined()
    expect(validarRecepcion({ ...valido, fecha: '2026-10-01' }, ahora).fecha).toBeUndefined()
  })

  it('las observaciones tienen tope de 500 caracteres', () => {
    expect(
      validarRecepcion({ ...valido, observaciones: 'x'.repeat(501) }, ahora).observaciones,
    ).toMatch(/más de 500 caracteres/)
  })
})
