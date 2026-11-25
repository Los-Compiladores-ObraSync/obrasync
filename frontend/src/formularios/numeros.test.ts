import { describe, expect, it, vi } from 'vitest'
import type { KeyboardEvent } from 'react'
import {
  aceptaTextoNumerico,
  bloquearTeclasDecimales,
  errorDeFormatoNumerico,
  esPresentacionEntera,
  esUnidadEntera,
  MENSAJE_SIN_COMA,
  MENSAJE_SOLO_ENTEROS,
} from './numeros'

describe('esUnidadEntera', () => {
  it('solo las bolsas son enteras', () => {
    expect(esUnidadEntera('BOLSA')).toBe(true)
    expect(esUnidadEntera(' bolsa ')).toBe(true)
    for (const otra of ['KG', 'M3', 'UNIDAD', '', null, undefined]) expect(esUnidadEntera(otra)).toBe(false)
  })
})

describe('esPresentacionEntera', () => {
  it('es entera si el material va en bolsas o la presentación empieza con "bolsa"', () => {
    expect(esPresentacionEntera('caja de 10', 'BOLSA')).toBe(true)
    expect(esPresentacionEntera('Bolsa de 25 kg', 'KG')).toBe(true)
    expect(esPresentacionEntera('bolsas de 50 kg')).toBe(true)
    expect(esPresentacionEntera('balde de 20 litros', 'UNIDAD')).toBe(false)
    expect(esPresentacionEntera('bolsón de 1 m3', 'M3')).toBe(false)
  })
})

describe('aceptaTextoNumerico', () => {
  it('nunca acepta comas', () => {
    expect(aceptaTextoNumerico('1,4')).toBe(false)
    expect(aceptaTextoNumerico('1,')).toBe(false)
    expect(aceptaTextoNumerico('1.4')).toBe(true)
    expect(aceptaTextoNumerico('')).toBe(true)
  })

  it('en un campo entero tampoco acepta el punto', () => {
    expect(aceptaTextoNumerico('1.4', true)).toBe(false)
    expect(aceptaTextoNumerico('14', true)).toBe(true)
  })
})

describe('bloquearTeclasDecimales', () => {
  const tecla = (key: string) => {
    const preventDefault = vi.fn()
    bloquearTeclasDecimales({ key, preventDefault } as unknown as KeyboardEvent<HTMLInputElement>)
    return preventDefault
  }

  it('frena la coma siempre y el punto solo en los enteros', () => {
    expect(tecla(',')).toHaveBeenCalled()
    expect(tecla('.')).not.toHaveBeenCalled()
    expect(tecla('7')).not.toHaveBeenCalled()
    const preventDefault = vi.fn()
    bloquearTeclasDecimales({ key: '.', preventDefault } as unknown as KeyboardEvent<HTMLInputElement>, true)
    expect(preventDefault).toHaveBeenCalled()
  })
})

describe('errorDeFormatoNumerico', () => {
  it('explica qué está mal', () => {
    expect(errorDeFormatoNumerico('1,4')).toBe(MENSAJE_SIN_COMA)
    expect(errorDeFormatoNumerico('1.4', true)).toBe(MENSAJE_SOLO_ENTEROS)
    expect(errorDeFormatoNumerico('1.4')).toBeUndefined()
    expect(errorDeFormatoNumerico('3', true)).toBeUndefined()
  })
})
