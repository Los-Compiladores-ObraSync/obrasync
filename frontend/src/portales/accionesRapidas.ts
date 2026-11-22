// Accesos rápidos de la pantalla de Inicio: lo que más se hace en cada rol, a un toque.
// Cada acción lleva a una pantalla que ya existe (rutas de App.tsx). Si cambia una ruta,
// hay que cambiarla acá también (hay una prueba que avisa si dejan de coincidir con el rol).
// Qué acciones ve cada rol es una decisión de producto: confirmarla con el PO.

export interface AccionRapida {
  /** Texto del botón. */
  texto: string
  /** Ruta de la pantalla a la que lleva. */
  ruta: string
  /** Se muestra como tarjeta grande y destacada arriba de los demás botones (hoy: el asistente de IA). */
  destacada?: boolean
  /** Frase corta opcional que acompaña a una acción destacada. */
  descripcion?: string
}

// Chat con el asistente de IA (#138): arma la requisición por texto y el usuario la confirma.
const ASISTENTE: AccionRapida = {
  texto: 'Asistente',
  ruta: '/obra/asistente',
  destacada: true,
}
// El Comprador usa el mismo asistente en su portal (#141): consulta requisiciones y arma la aprobación para confirmar.
const ASISTENTE_COMPRAS: AccionRapida = {
  texto: 'Asistente',
  ruta: '/compras/asistente',
  destacada: true,
}
const NUEVA_REQUISICION: AccionRapida = {
  texto: 'Nueva requisición',
  ruta: '/obra/requisicion/nueva',
}
const RECIBIR_MERCADERIA: AccionRapida = {
  texto: 'Recibir mercadería',
  ruta: '/obra/recepcion',
}
// El Capataz entra por /obra y el Comprador por /compras: es la misma pantalla en dos portales.
// El Capataz ve solo las suyas; el Comprador y el Admin ven todas.
const HISTORIAL_REQUISICIONES_OBRA: AccionRapida = {
  texto: 'Historial de requisiciones',
  ruta: '/obra/requisiciones',
}
const HISTORIAL_REQUISICIONES_COMPRAS: AccionRapida = {
  texto: 'Historial de requisiciones',
  ruta: '/compras/requisiciones/historial',
}
const NUEVA_OBRA: AccionRapida = {
  texto: 'Nueva obra',
  ruta: '/compras/obras/nueva',
}
const VER_OBRAS: AccionRapida = {
  texto: 'Ver obras',
  ruta: '/compras/obras',
}
// Despacho de órdenes de compra (#205): Compras marca cuándo el proveedor envió la mercadería.
const ORDENES_DE_COMPRA: AccionRapida = {
  texto: 'Órdenes de compra',
  ruta: '/compras/ordenes',
}
// Partidas presupuestarias por obra y etapa (#161). Misma pantalla en dos portales: el Capataz la ve
// desde el suyo (solo consulta) y Comprador y Admin desde Gestión y Compras (también cargan partidas).
const PRESUPUESTO_OBRA: AccionRapida = {
  texto: 'Presupuesto',
  ruta: '/obra/presupuesto',
}
const PRESUPUESTO_COMPRAS: AccionRapida = {
  texto: 'Presupuesto',
  ruta: '/compras/presupuesto',
}
// Carga masiva del catálogo desde un CSV (#215): solo Comprador y Admin (los roles del alta de insumos).
const CARGAR_INSUMOS: AccionRapida = {
  texto: 'Cargar insumos (CSV)',
  ruta: '/compras/insumos/carga',
}
const LICITACIONES_ABIERTAS: AccionRapida = {
  texto: 'Licitaciones abiertas',
  ruta: '/proveedores',
}
const LICITACIONES_Y_COTIZACIONES: AccionRapida = {
  texto: 'Licitaciones y cotizaciones',
  ruta: '/compras/licitaciones',
}
// Portal del Proveedor (#230): historial de lo que cotizó y las órdenes que ganó (donde marca el despacho).
const MIS_COTIZACIONES: AccionRapida = {
  texto: 'Historial de cotizaciones',
  ruta: '/proveedores/mis-cotizaciones',
}
const MIS_ORDENES_PROVEEDOR: AccionRapida = {
  texto: 'Mis órdenes',
  ruta: '/proveedores/mis-ordenes',
}

// Sin barra de pestañas: el Inicio de cada rol trae todo lo que ese rol hace.
// Quien aprueba requisiciones (Comprador y Admin) ve primero la tarjeta "Tenés N requisiciones para
// aprobar" (ResumenAprobacion), así que acá van solo las opciones secundarias.
const ACCIONES_POR_ROL: Record<string, readonly AccionRapida[]> = {
  Capataz: [
    ASISTENTE,
    NUEVA_REQUISICION,
    RECIBIR_MERCADERIA,
    HISTORIAL_REQUISICIONES_OBRA,
    PRESUPUESTO_OBRA,
  ],
  Comprador: [
    ASISTENTE_COMPRAS,
    HISTORIAL_REQUISICIONES_COMPRAS,
    NUEVA_OBRA,
    VER_OBRAS,
    LICITACIONES_Y_COTIZACIONES,
    ORDENES_DE_COMPRA,
    PRESUPUESTO_COMPRAS,
    CARGAR_INSUMOS,
  ],
  Admin: [
    ASISTENTE,
    NUEVA_REQUISICION,
    RECIBIR_MERCADERIA,
    HISTORIAL_REQUISICIONES_COMPRAS,
    NUEVA_OBRA,
    VER_OBRAS,
    LICITACIONES_Y_COTIZACIONES,
    ORDENES_DE_COMPRA,
    PRESUPUESTO_COMPRAS,
    CARGAR_INSUMOS,
    LICITACIONES_ABIERTAS,
  ],

  // El Inicio del Proveedor es la lista de licitaciones abiertas; debajo van sus dos accesos.
  Proveedor: [MIS_COTIZACIONES, MIS_ORDENES_PROVEEDOR],
}

/** Accesos rápidos de un rol (lista vacía si el rol no tiene o no es conocido). */
export function accionesParaRol(rol: string): readonly AccionRapida[] {
  return ACCIONES_POR_ROL[rol] ?? []
}
