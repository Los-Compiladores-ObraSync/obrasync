import { describe, expect, it } from 'vitest'
import { errorDePasswordNueva, LARGO_MINIMO_PASSWORD } from './validacionPassword'

describe('errorDePasswordNueva (política #168: mínimo 8 y al menos un número)', () => {
  it('pide al menos 8 caracteres', () => {
    expect(LARGO_MINIMO_PASSWORD).toBe(8)
    expect(errorDePasswordNueva('abc1234')).toBe('La contraseña debe tener al menos 8 caracteres.')
  })

  it('pide al menos un número', () => {
    expect(errorDePasswordNueva('sinnumero')).toBe('La contraseña debe incluir al menos un número.')
  })

  it('acepta una contraseña de 8 o más caracteres con un número', () => {
    expect(errorDePasswordNueva('abcdefg1')).toBeUndefined()
  })
})
