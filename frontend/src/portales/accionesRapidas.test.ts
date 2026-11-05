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
  it('el Capataz crea requisiciones, recibe mercadería y ve su historial', () => {
    expect(accionesParaRol('Capataz').map((a) => a.texto)).toEqual([
      'Nueva requisición',
      'Recibir mercadería',
      'Historial de requisiciones',
    ])
  })

  it('el Comprador ve el historial y las obras, con "Nueva obra" a un toque (aprobar es la tarjeta de arriba)', () => {
    expect(accionesParaRol('Comprador').map((a) => a.texto)).toEqual([
      'Historial de requisiciones',
      'Nueva obra',
      'Ver obras',
    ])
  })

  it('el Admin ve todo lo que hacen los otros roles, incluidas las licitaciones abiertas', () => {
    expect(accionesParaRol('Admin').map((a) => a.texto)).toEqual([
      'Nueva requisición',
      'Recibir mercadería',
      'Historial de requisiciones',
      'Nueva obra',
      'Ver obras',
      'Licitaciones abiertas',
    ])
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
