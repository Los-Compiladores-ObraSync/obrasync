import { describe, expect, it } from 'vitest'
import { validarEmail } from './validacionEmail'

describe('validarEmail', () => {
  it('acepta un email con formato válido', () => {
    expect(validarEmail('usuario@obrasync.com')).toBeUndefined()
  })

  it('ignora los espacios de los costados', () => {
    expect(validarEmail('  usuario@obrasync.com  ')).toBeUndefined()
  })

  it('pide el email si está vacío o solo tiene espacios', () => {
    expect(validarEmail('')).toBe('El email es obligatorio.')
    expect(validarEmail('   ')).toBe('El email es obligatorio.')
  })

  it('rechaza un formato inválido', () => {
    expect(validarEmail('sin-arroba')).toBe('Ingresá un email válido.')
    expect(validarEmail('a@b')).toBe('Ingresá un email válido.')
    expect(validarEmail('a b@c.com')).toBe('Ingresá un email válido.')
  })
})
