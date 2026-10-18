// Tabla de portales: es la ÚNICA fuente de verdad de "qué rol entra a qué portal".
// El menú, las rutas protegidas y las pruebas leen de acá. Si mañana el Product Owner
// cambia una regla (por ejemplo, qué ve el Admin), se cambia una línea de esta tabla.

export type PortalId = 'obra' | 'compras' | 'proveedores'

export interface Portal {
  id: PortalId
  /** Ruta base del portal dentro de la app. */
  ruta: string
  /** Nombre que se muestra en el menú y como título de la pantalla. */
  nombre: string
  /** Roles que pueden entrar (los mismos nombres que emite el JWT). */
  roles: readonly string[]
}

// Capataz → Obra/Terreno, Comprador → Gestión y Compras, Proveedor → Proveedores
// (tarjeta #26). El Admin entra a los tres: decisión del Scrum Master porque la tarjeta
// no define un portal para ese rol; pendiente de confirmar con el PO.
export const PORTALES: readonly Portal[] = [
  { id: 'obra', ruta: '/obra', nombre: 'Obra / Terreno', roles: ['Capataz', 'Admin'] },
  {
    id: 'compras',
    ruta: '/compras',
    nombre: 'Gestión y Compras',
    roles: ['Comprador', 'Admin'],
  },
  {
    id: 'proveedores',
    ruta: '/proveedores',
    nombre: 'Proveedores',
    roles: ['Proveedor', 'Admin'],
  },
]

/** Portales a los que puede entrar un rol (lista vacía si el rol no es conocido). */
export function portalesParaRol(rol: string): Portal[] {
  return PORTALES.filter((portal) => portal.roles.includes(rol))
}

export function puedeEntrar(rol: string, portalId: PortalId): boolean {
  return portalesParaRol(rol).some((portal) => portal.id === portalId)
}
