import { ApiError } from '../api/ApiError'
import { apiFetch } from '../api/client'
import type { InsumoCatalogo } from '../requisiciones/requisicionesTipos'
import type { CrearInsumoRequest } from './catalogoTipos'
import type { InsumoParaCargar } from './csvInsumos'

/**
 * Da de alta un insumo en el catálogo (#215). Roles: Comprador y Admin.
 * Fuente: POST /api/catalogo/insumos
 *
 * Errores: 400 datos inválidos o divisa no permitida; 401 sin sesión; 403 rol sin permiso;
 * 409 el código ya existe.
 */
export function crearInsumo(datos: CrearInsumoRequest): Promise<InsumoCatalogo> {
  return apiFetch<InsumoCatalogo>('/api/catalogo/insumos', {
    method: 'POST',
    body: JSON.stringify(datos),
  })
}

export interface FilaFallida {
  fila: number
  codigo: string
  motivo: string
}

export interface ResultadoCarga {
  cargados: number
  fallidas: FilaFallida[]
  /** Filas que no se llegaron a enviar porque la carga se cortó. */
  sinEnviar: number
  /** Por qué se cortó la carga (sin permiso, sin conexión, cancelada). null si se procesó todo. */
  motivoCorte: string | null
}

export interface OpcionesCarga {
  alProgreso?: (hechas: number, total: number) => void
  /** Se consulta antes de cada fila: si devuelve true, se corta (por ejemplo, al salir de la pantalla). */
  estaCancelado?: () => boolean
}

/**
 * Envía los insumos de a uno, en orden. Por qué de a uno y no todos juntos: el endpoint existente recibe
 * un solo insumo, y así cada fila tiene su propio resultado (la 409 de un código repetido no frena al resto).
 *
 * Los errores propios de una fila (400, 409 y fallas del servidor) se anotan y se sigue. Los errores que
 * afectarían a todas las filas (sin permiso, sin sesión, sin conexión) cortan la carga: seguir mandando
 * cientos de pedidos que van a fallar igual solo haría esperar.
 */
export async function cargarInsumos(
  insumos: readonly InsumoParaCargar[],
  { alProgreso, estaCancelado }: OpcionesCarga = {},
): Promise<ResultadoCarga> {
  const total = insumos.length
  const fallidas: FilaFallida[] = []
  let cargados = 0
  let motivoCorte: string | null = null
  let procesadas = 0

  for (const { fila, datos } of insumos) {
    if (estaCancelado?.()) {
      motivoCorte = 'Se canceló la carga.'
      break
    }
    try {
      await crearInsumo(datos)
      cargados += 1
    } catch (error) {
      if (!(error instanceof ApiError) || error.status === 0) {
        motivoCorte = 'Se cortó la conexión. Revisá tu internet y volvé a intentar con las filas que faltan.'
        break
      }
      if (error.status === 401) {
        motivoCorte = 'Tu sesión venció. Iniciá sesión de nuevo.'
        break
      }
      if (error.status === 403) {
        motivoCorte = 'Tu rol no tiene permiso para cargar insumos.'
        break
      }
      const motivoPorDefecto =
        error.status === 409
          ? 'Ya existe un insumo con ese código.'
          : error.status === 400
            ? 'El servidor no aceptó los datos de esta fila.'
            : 'El servidor no pudo guardar esta fila.'
      fallidas.push({ fila, codigo: datos.codigo, motivo: error.detail ?? motivoPorDefecto })
    }
    procesadas += 1
    alProgreso?.(procesadas, total)
  }

  return { cargados, fallidas, sinEnviar: total - procesadas, motivoCorte }
}
