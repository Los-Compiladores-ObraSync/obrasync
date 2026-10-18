import { describe, expect, it } from 'vitest'
import { PORTALES, portalesParaRol, puedeEntrar } from './portales'

function idsPara(rol: string): string[] {
  return portalesParaRol(rol).map((portal) => portal.id)
}

describe('portalesParaRol', () => {
  it('el Capataz entra solo a Obra / Terreno', () => {
    expect(idsPara('Capataz')).toEqual(['obra'])
  })

  it('el Comprador entra solo a Gestión y Compras', () => {
    expect(idsPara('Comprador')).toEqual(['compras'])
  })

  it('el Proveedor entra solo a Proveedores', () => {
    expect(idsPara('Proveedor')).toEqual(['proveedores'])
  })

  it('el Admin entra a los tres portales', () => {
    expect(idsPara('Admin')).toEqual(['obra', 'compras', 'proveedores'])
  })

  it('un rol desconocido no entra a ningún portal', () => {
    expect(idsPara('Desconocido')).toEqual([])
  })
})

describe('puedeEntrar', () => {
  it('permite entrar al portal propio y rechaza los ajenos', () => {
    expect(puedeEntrar('Capataz', 'obra')).toBe(true)
    expect(puedeEntrar('Capataz', 'compras')).toBe(false)
    expect(puedeEntrar('Proveedor', 'obra')).toBe(false)
  })
})

describe('PORTALES', () => {
  it('no repite ids ni rutas', () => {
    expect(new Set(PORTALES.map((p) => p.id)).size).toBe(PORTALES.length)
    expect(new Set(PORTALES.map((p) => p.ruta)).size).toBe(PORTALES.length)
  })
})
