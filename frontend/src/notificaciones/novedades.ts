import type { LicitacionResponseDto } from '../licitaciones/licitacionesTipos'
import type { RequisicionResponseDto } from '../requisiciones/requisicionesTipos'

/** Quién recibe avisos propios en la campana (#163). El Comprador y el Admin tienen los suyos
 *  (pendientes y órdenes bloqueadas) y no entran acá. */
export type TipoAvisos = 'capataz' | 'proveedor'

export interface Novedad {
  /** Id de lo que cambió: la requisición o la licitación. */
  id: string
  /** Lo que se recuerda al marcarla como vista: el estado (requisición) o "visto" (licitación).
   *  Así, si una requisición cambia de estado otra vez, vuelve a avisar. */
  marca: string
  tipo: 'requisicion-aprobada' | 'requisicion-rechazada' | 'licitacion-nueva'
  /** Número de la requisición (la licitación se nombra por la requisición de la que sale). */
  numero: string
  /** A dónde lleva el aviso. */
  ruta: string
}

/** Lo ya visto: id de la requisición o licitación → marca. */
export type Vistos = Record<string, string>

export function tipoDeAvisosParaRol(rol: string | undefined): TipoAvisos | null {
  if (rol === 'Capataz') return 'capataz'
  if (rol === 'Proveedor') return 'proveedor'
  return null
}

/** Clave de localStorage: una por rol y por usuario, para que dos personas que comparten el
 *  navegador no se pisen los avisos. */
export function claveVistos(tipo: TipoAvisos, usuarioId: string): string {
  return `obrasync_avisos_vistos_${tipo}_${usuarioId}`
}

/** Lee lo ya visto. null = nunca se guardó nada (primera vez en este navegador) o no se pudo leer. */
export function leerVistos(clave: string): Vistos | null {
  try {
    const texto = localStorage.getItem(clave)
    if (!texto) return null
    const dato: unknown = JSON.parse(texto)
    if (dato === null || typeof dato !== 'object' || Array.isArray(dato)) return null
    const limpio: Vistos = {}
    for (const [id, marca] of Object.entries(dato)) {
      if (typeof marca === 'string') limpio[id] = marca
    }
    return limpio
  } catch {
    return null
  }
}

/** Guarda lo ya visto. Si el navegador no deja escribir (modo privado, sin espacio), el aviso
 *  se repite en la próxima visita, pero la app sigue andando. */
export function guardarVistos(clave: string, vistos: Vistos): void {
  try {
    localStorage.setItem(clave, JSON.stringify(vistos))
  } catch {
    // Se ignora a propósito: es una comodidad, no un dato importante.
  }
}

/** Primera vez que el Capataz entra en este navegador: lo que ya estaba aprobado o rechazado se
 *  da por visto, para no llenar la campana de avisos viejos. Solo avisa lo que cambie desde ahora. */
export function vistosIniciales(requisiciones: RequisicionResponseDto[]): Vistos {
  const vistos: Vistos = {}
  for (const req of requisiciones) {
    if (req.estado !== 'Pendiente') vistos[req.requisicionId] = req.estado
  }
  return vistos
}

function momentoDelCambio(req: RequisicionResponseDto): number {
  const fecha = req.estado === 'Aprobada' ? req.fechaAprobacion : req.fechaRechazo
  const ms = Date.parse(fecha ?? req.fechaCreacion)
  return Number.isNaN(ms) ? 0 : ms
}

/** Capataz: requisiciones que ya no están pendientes y cuyo estado todavía no vio. Las más recientes primero. */
export function novedadesDeRequisiciones(
  requisiciones: RequisicionResponseDto[],
  vistos: Vistos,
): Novedad[] {
  return requisiciones
    .filter((req) => req.estado !== 'Pendiente' && vistos[req.requisicionId] !== req.estado)
    .sort((a, b) => momentoDelCambio(b) - momentoDelCambio(a))
    .map((req) => ({
      id: req.requisicionId,
      marca: req.estado,
      tipo: req.estado === 'Aprobada' ? 'requisicion-aprobada' : 'requisicion-rechazada',
      numero: req.numeroRequisicion,
      ruta: `/obra/requisiciones/${encodeURIComponent(req.requisicionId)}`,
    }))
}

/** Proveedor: licitaciones abiertas que todavía no cotizó y de las que no se le avisó. GET /api/licitaciones
 *  ya devuelve solo las abiertas. Las más nuevas primero. */
export function novedadesDeLicitaciones(
  licitaciones: LicitacionResponseDto[],
  vistos: Vistos,
): Novedad[] {
  return licitaciones
    .filter((lic) => lic.yaCotizada !== true && vistos[lic.licitacionId] === undefined)
    .sort((a, b) => Date.parse(b.fechaCreacion) - Date.parse(a.fechaCreacion))
    .map((lic) => ({
      id: lic.licitacionId,
      marca: 'visto',
      tipo: 'licitacion-nueva' as const,
      numero: lic.numeroRequisicion ?? 'Licitación',
      ruta: `/proveedores/licitaciones/${encodeURIComponent(lic.licitacionId)}`,
    }))
}
