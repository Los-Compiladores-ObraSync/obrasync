import { describe, expect, it } from 'vitest'
import { validarRegistro } from './validacionRegistro'

describe('validarRegistro', () => {
  it('marca campos obligatorios y email inválido', () => {
    expect(validarRegistro('  ', 'no-es-email', '')).toEqual({
      nombreCompleto: 'El nombre completo es obligatorio.',
      email: 'Ingresá un email válido.',
      password: 'La contraseña es obligatoria.',
    })
  })

  it('exige ocho caracteres y al menos un número', () => {
    expect(validarRegistro('Ana', 'ana@empresa.com', 'abc123')).toMatchObject({
      password: 'La contraseña debe tener al menos 8 caracteres.',
    })
    expect(validarRegistro('Ana', 'ana@empresa.com', 'abcdefgh')).toMatchObject({
      password: 'La contraseña debe incluir al menos un número.',
    })
    expect(validarRegistro('Ana', 'ana@empresa.com', 'abcde123')).toEqual({})
  })
})
