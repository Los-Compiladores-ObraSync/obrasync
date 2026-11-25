import { describe, expect, it } from 'vitest'
import { digitosParaWhatsApp, urlWhatsApp, validarTelefono } from './telefono'

describe('validarTelefono', () => {
  it('es opcional', () => {
    expect(validarTelefono('')).toBeUndefined()
    expect(validarTelefono('   ')).toBeUndefined()
  })

  it('acepta números con espacios, guiones, paréntesis y un + inicial', () => {
    expect(validarTelefono('+54 9 11 5555-1234')).toBeUndefined()
    expect(validarTelefono('(011) 5555-1234')).toBeUndefined()
  })

  it('rechaza letras y largos imposibles', () => {
    expect(validarTelefono('llamame')).toMatch(/solo números/)
    expect(validarTelefono('1234567')).toMatch(/solo números/)
    expect(validarTelefono('+54'.padEnd(25, '1'))).toMatch(/solo números/)
  })
})

describe('digitosParaWhatsApp', () => {
  it('deja solo los dígitos cuando hay código de país', () => {
    expect(digitosParaWhatsApp('+54 9 11 5555-1234')).toBe('5491155551234')
    expect(digitosParaWhatsApp('0054 9 11 5555 1234')).toBe('5491155551234')
  })

  it('no inventa un país: sin + ni 00 devuelve null', () => {
    expect(digitosParaWhatsApp('11 5555-1234')).toBeNull()
    expect(digitosParaWhatsApp(null)).toBeNull()
    expect(digitosParaWhatsApp(undefined)).toBeNull()
    expect(digitosParaWhatsApp('')).toBeNull()
  })
})

describe('urlWhatsApp', () => {
  it('va directo al chat si hay número y codifica el mensaje', () => {
    expect(urlWhatsApp('+54 9 11 5555-1234', 'Hola & chau')).toBe('https://wa.me/5491155551234?text=Hola%20%26%20chau')
  })

  it('sin número deja elegir el contacto', () => {
    expect(urlWhatsApp(null, 'Hola')).toBe('https://wa.me/?text=Hola')
  })
})
