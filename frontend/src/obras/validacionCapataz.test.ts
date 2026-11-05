import { describe, expect, it } from 'vitest'
import { hayErroresCapataz, validarCapataz } from './validacionCapataz'

const valido = { nombreCompleto: 'Ana López', email: 'ana@obrasync.com', password: 'secreta1' }

describe('validarCapataz', () => {
  it('no marca errores con datos válidos', () => {
    expect(hayErroresCapataz(validarCapataz(valido))).toBe(false)
  })

  it('exige nombre, email y contraseña', () => {
    const errores = validarCapataz({ nombreCompleto: '  ', email: '', password: '' })
    expect(errores.nombreCompleto).toBeDefined()
    expect(errores.email).toBeDefined()
    expect(errores.password).toBeDefined()
  })

  it('rechaza un email mal formado', () => {
    expect(validarCapataz({ ...valido, email: 'ana@' }).email).toBe('Ingresá un email válido.')
  })

  it('rechaza una contraseña de menos de 6 caracteres y acepta justo 6', () => {
    expect(validarCapataz({ ...valido, password: '12345' }).password).toContain('al menos 6')
    expect(validarCapataz({ ...valido, password: '123456' }).password).toBeUndefined()
  })
})
