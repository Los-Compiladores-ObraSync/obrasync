import { beforeEach, describe, expect, it } from 'vitest'
import type { LicitacionResponseDto } from '../licitaciones/licitacionesTipos'
import type { RequisicionResponseDto } from '../requisiciones/requisicionesTipos'
import {
  claveVistos,
  guardarVistos,
  leerVistos,
  novedadesDeLicitaciones,
  novedadesDeRequisiciones,
  tipoDeAvisosParaRol,
  vistosIniciales,
} from './novedades'

function requisicion(datos: Partial<RequisicionResponseDto>): RequisicionResponseDto {
  return {
    requisicionId: 'r-1',
    numeroRequisicion: 'REQ-2026-0001',
    obraId: 'o-1',
    solicitanteId: 'u-1',
    estado: 'Pendiente',
    fechaCreacion: '2026-11-01T10:00:00Z',
    items: [],
    ...datos,
  }
}

function licitacion(datos: Partial<LicitacionResponseDto>): LicitacionResponseDto {
  return {
    licitacionId: 'l-1',
    requisicionId: 'r-1',
    numeroRequisicion: 'REQ-2026-0002',
    obraId: 'o-1',
    estado: 'Abierta',
    fechaCreacion: '2026-11-01T10:00:00Z',
    fechaLimiteCotizacion: '2026-11-30T10:00:00Z',
    items: [],
    ...datos,
  }
}

describe('tipoDeAvisosParaRol', () => {
  it('solo el Capataz y el Proveedor tienen avisos propios', () => {
    expect(tipoDeAvisosParaRol('Capataz')).toBe('capataz')
    expect(tipoDeAvisosParaRol('Proveedor')).toBe('proveedor')
    expect(tipoDeAvisosParaRol('Comprador')).toBeNull()
    expect(tipoDeAvisosParaRol('Admin')).toBeNull()
    expect(tipoDeAvisosParaRol(undefined)).toBeNull()
  })
})

describe('novedadesDeRequisiciones (Capataz)', () => {
  it('avisa de las aprobadas y rechazadas que no vio, y no de las pendientes', () => {
    const lista = [
      requisicion({ requisicionId: 'a', estado: 'Aprobada', fechaAprobacion: '2026-11-02T10:00:00Z' }),
      requisicion({ requisicionId: 'b', estado: 'Rechazada', fechaRechazo: '2026-11-03T10:00:00Z' }),
      requisicion({ requisicionId: 'c', estado: 'Pendiente' }),
    ]

    const novedades = novedadesDeRequisiciones(lista, {})

    // La más reciente primero.
    expect(novedades.map((n) => [n.id, n.tipo])).toEqual([
      ['b', 'requisicion-rechazada'],
      ['a', 'requisicion-aprobada'],
    ])
    expect(novedades[0].ruta).toBe('/obra/requisiciones/b')
  })

  it('no repite lo ya visto, pero vuelve a avisar si el estado cambia otra vez', () => {
    const aprobada = requisicion({ requisicionId: 'a', estado: 'Aprobada' })
    expect(novedadesDeRequisiciones([aprobada], { a: 'Aprobada' })).toEqual([])

    const rechazadaDespues = requisicion({ requisicionId: 'a', estado: 'Rechazada' })
    expect(novedadesDeRequisiciones([rechazadaDespues], { a: 'Aprobada' })).toHaveLength(1)
  })
})

describe('vistosIniciales', () => {
  it('da por vistas las ya resueltas y deja pasar las pendientes, para que avisen cuando cambien', () => {
    const lista = [
      requisicion({ requisicionId: 'a', estado: 'Aprobada' }),
      requisicion({ requisicionId: 'b', estado: 'Pendiente' }),
    ]

    const vistos = vistosIniciales(lista)

    expect(vistos).toEqual({ a: 'Aprobada' })
    // La pendiente, cuando se apruebe, es una novedad.
    const despues = [lista[0], requisicion({ requisicionId: 'b', estado: 'Aprobada' })]
    expect(novedadesDeRequisiciones(despues, vistos).map((n) => n.id)).toEqual(['b'])
  })
})

describe('novedadesDeLicitaciones (Proveedor)', () => {
  it('avisa de las abiertas que no cotizó y no vio', () => {
    const lista = [
      licitacion({ licitacionId: 'x', fechaCreacion: '2026-11-01T10:00:00Z' }),
      licitacion({ licitacionId: 'y', yaCotizada: true }),
      licitacion({ licitacionId: 'z', fechaCreacion: '2026-11-05T10:00:00Z' }),
    ]

    const novedades = novedadesDeLicitaciones(lista, {})

    expect(novedades.map((n) => n.id)).toEqual(['z', 'x'])
    expect(novedades[0].ruta).toBe('/proveedores/licitaciones/z')
    expect(novedades[0].numero).toBe('REQ-2026-0002')
  })

  it('no avisa de las que ya vio', () => {
    const lista = [licitacion({ licitacionId: 'x' })]
    expect(novedadesDeLicitaciones(lista, { x: 'visto' })).toEqual([])
  })
})

describe('guardar y leer lo visto', () => {
  beforeEach(() => localStorage.clear())

  it('guarda y recupera, con una clave distinta por rol y por usuario', () => {
    const clave = claveVistos('capataz', 'u-1')
    guardarVistos(clave, { a: 'Aprobada' })

    expect(leerVistos(clave)).toEqual({ a: 'Aprobada' })
    expect(leerVistos(claveVistos('capataz', 'u-2'))).toBeNull()
    expect(leerVistos(claveVistos('proveedor', 'u-1'))).toBeNull()
  })

  it('si lo guardado está roto, se trata como primera vez', () => {
    const clave = claveVistos('capataz', 'u-1')
    localStorage.setItem(clave, '{no es json')
    expect(leerVistos(clave)).toBeNull()

    localStorage.setItem(clave, '[1,2]')
    expect(leerVistos(clave)).toBeNull()
  })
})
