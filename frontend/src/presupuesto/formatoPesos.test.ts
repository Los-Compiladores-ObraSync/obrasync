import { describe, expect, it } from 'vitest'
import { formatearPesos } from './formatoPesos'

describe('formatearPesos', () => {
  it('formatea números positivos en pesos argentinos con separadores locales', () => {
    const resultado = formatearPesos(1250000)
    // es-AR usa punto para miles y coma para decimales con signo $
    expect(resultado).toMatch(/\$\s*1\.250\.000,00/)
  })

  it('formatea el cero correctamente', () => {
    const resultado = formatearPesos(0)
    expect(resultado).toMatch(/\$\s*0,00/)
  })

  it('formatea montos negativos con signo negativo y separadores locales', () => {
    const resultado = formatearPesos(-1200000)
    // Puede formatearse como -$ 1.200.000,00 o $-1.200.000,00 según el motor
    expect(resultado).toMatch(/-?\$?\s*-?1\.200\.000,00/)
    expect(resultado).toContain('-')
    expect(resultado).toContain('1.200.000,00')
  })

  it('formatea decimales respetando dos cifras', () => {
    const resultado = formatearPesos(4500.5)
    expect(resultado).toMatch(/\$\s*4\.500,50/)
  })
})
