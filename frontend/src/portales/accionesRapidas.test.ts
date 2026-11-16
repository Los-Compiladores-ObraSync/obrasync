import { describe, expect, it } from 'vitest'
import { accionesParaRol } from './accionesRapidas'
import { puedeEntrar, type PortalId } from './portales'

// A qué portal pertenece cada ruta: sirve para comprobar que el rol puede entrar.
function portalDeRuta(ruta: string): PortalId {
  if (ruta.startsWith('/obra')) return 'obra'
  if (ruta.startsWith('/compras')) return 'compras'
  return 'proveedores'
}

describe('accionesParaRol', () => {
  it('el Capataz usa el asistente, crea requisiciones, recibe mercadería y ve su historial', () => {
    expect(accionesParaRol('Capataz').map((a) => a.texto)).toEqual([
      'Asistente',
      'Nueva requisición',
      'Recibir mercadería',
      'Historial de requisiciones',
    ])
  })

  it('el Comprador usa el asistente y ve el historial y las obras, con "Nueva obra" a un toque (aprobar es la tarjeta de arriba)', () => {
    expect(accionesParaRol('Comprador').map((a) => a.texto)).toEqual([
      'Asistente',
      'Historial de requisiciones',
      'Nueva obra',
      'Ver obras',
      'Licitaciones y cotizaciones',
    ])
  })

  it('el Admin ve todo lo que hacen los otros roles, incluidas las licitaciones abiertas', () => {
    expect(accionesParaRol('Admin').map((a) => a.texto)).toEqual([
      'Asistente',
      'Nueva requisición',
      'Recibir mercadería',
      'Historial de requisiciones',
      'Nueva obra',
      'Ver obras',
      'Licitaciones y cotizaciones',
      'Licitaciones abiertas',
    ])
  })

  it('el asistente es la única acción destacada', () => {
    for (const rol of ['Capataz', 'Comprador', 'Admin']) {
      const destacadas = accionesParaRol(rol).filter((a) => a.destacada)
      expect(destacadas.map((a) => a.texto)).toEqual(['Asistente'])
    }
  })

  it('el Comprador entra al asistente por su portal (/compras/asistente) y el Capataz por el suyo', () => {
    expect(accionesParaRol('Comprador')[0].ruta).toBe('/compras/asistente')
    expect(accionesParaRol('Capataz')[0].ruta).toBe('/obra/asistente')
  })

  it('el Proveedor no tiene botones (su Inicio es la lista de licitaciones) y un rol desconocido tampoco', () => {
    expect(accionesParaRol('Proveedor')).toEqual([])
    expect(accionesParaRol('Inventado')).toEqual([])
  })

  it.each(['Capataz', 'Comprador', 'Admin', 'Proveedor'])(
    'todas las acciones de %s llevan a un portal al que ese rol puede entrar',
    (rol) => {
      for (const accion of accionesParaRol(rol)) {
        expect(puedeEntrar(rol, portalDeRuta(accion.ruta))).toBe(true)
      }
    },
  )
})
