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
  it('el Capataz crea requisiciones', () => {
    expect(accionesParaRol('Capataz').map((a) => a.texto)).toEqual(['Nueva requisición'])
  })

  it('el Comprador aprueba requisiciones y crea obras', () => {
    expect(accionesParaRol('Comprador').map((a) => a.texto)).toEqual([
      'Aprobar requisiciones',
      'Crear una obra nueva',
    ])
  })

  it('el Proveedor y un rol desconocido no tienen accesos rápidos', () => {
    expect(accionesParaRol('Proveedor')).toEqual([])
    expect(accionesParaRol('Inventado')).toEqual([])
  })

  it.each(['Capataz', 'Comprador', 'Admin'])(
    'todas las acciones de %s llevan a un portal al que ese rol puede entrar',
    (rol) => {
      for (const accion of accionesParaRol(rol)) {
        expect(puedeEntrar(rol, portalDeRuta(accion.ruta))).toBe(true)
      }
    },
  )
})
