// Accesos rápidos de la pantalla de Inicio: lo que más se hace en cada rol, a un toque.
// Cada acción lleva a una pantalla que ya existe (rutas de App.tsx). Si cambia una ruta,
// hay que cambiarla acá también (hay una prueba que avisa si dejan de coincidir con el rol).
// Qué acciones ve cada rol es una decisión de producto: confirmarla con el PO.

export interface AccionRapida {
  /** Texto del botón. */
  texto: string
  /** Ruta de la pantalla a la que lleva. */
  ruta: string
}

const NUEVA_REQUISICION: AccionRapida = {
  texto: 'Nueva requisición',
  ruta: '/obra/requisicion/nueva',
}
const APROBAR_REQUISICIONES: AccionRapida = {
  texto: 'Aprobar requisiciones',
  ruta: '/compras/requisiciones',
}
const CREAR_OBRA: AccionRapida = {
  texto: 'Crear una obra nueva',
  ruta: '/compras/obras/nueva',
}

const ACCIONES_POR_ROL: Record<string, readonly AccionRapida[]> = {
  Capataz: [NUEVA_REQUISICION],
  Comprador: [APROBAR_REQUISICIONES, CREAR_OBRA],
  Admin: [NUEVA_REQUISICION, APROBAR_REQUISICIONES, CREAR_OBRA],
  // El portal de Proveedores todavía no tiene pantallas: por ahora no hay accesos rápidos.
  Proveedor: [],
}

/** Accesos rápidos de un rol (lista vacía si el rol no tiene o no es conocido). */
export function accionesParaRol(rol: string): readonly AccionRapida[] {
  return ACCIONES_POR_ROL[rol] ?? []
}
