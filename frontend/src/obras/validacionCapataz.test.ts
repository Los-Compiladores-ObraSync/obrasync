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

  it('rechaza una contraseña de menos de 8 caracteres o sin número, y acepta una válida', () => {
    expect(validarCapataz({ ...valido, password: '1234567' }).password).toContain('al menos 8')
    expect(validarCapataz({ ...valido, password: 'sinnumero' }).password).toContain('al menos un número')
    expect(validarCapataz({ ...valido, password: 'secreta12' }).password).toBeUndefined()
  })
})
