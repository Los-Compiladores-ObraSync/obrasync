import { ApiError, type ProblemDetails } from './ApiError'
import { API_BASE_URL } from './config'
import type { PaginaDe } from './paginacion'

let token: string | null = null
let onUnauthorized: (() => void) | null = null

export function setToken(value: string | null): void {
  token = value
}

/** Registra un callback que se ejecuta cuando un pedido autenticado recibe un 401.
 *  AuthContext lo usa para cerrar la sesión automáticamente si el token expiró. */
export function setOnUnauthorized(callback: (() => void) | null): void {
  onUnauthorized = callback
}

function newCorrelationId(): string {
  if (typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID()
  }
  return `${Date.now().toString(16)}-${Math.random().toString(16).slice(2)}`
}

/** Tiempo máximo de un pedido normal (#199). Con mala señal en la obra un pedido podía quedar colgado para siempre. */
export const TIEMPO_LIMITE_MS = 15_000
/** El asistente tarda más: el servidor le da hasta 45 s, así que el cliente espera un poco más que eso. */
export const TIEMPO_LIMITE_ASISTENTE_MS = 60_000

/** Opciones de fetch más el tiempo límite propio de ObraSync. */
export interface OpcionesApi extends RequestInit {
  /** Milisegundos máximos del pedido completo (respuesta y cuerpo). Por defecto TIEMPO_LIMITE_MS. */
  tiempoLimiteMs?: number
}

function errorDeTiempoAgotado(): ApiError {
  return new ApiError(0, {
    title: 'Sin conexión',
    detail: 'No pudimos conectarnos, reintentá.',
  })
}

interface RespuestaCruda {
  datos: unknown
  encabezados: Headers
}

async function ejecutar(
  path: string,
  opciones: OpcionesApi,
): Promise<RespuestaCruda> {
  const { tiempoLimiteMs = TIEMPO_LIMITE_MS, signal: senalExterna, ...options } = opciones
  const headers = new Headers(options.headers)
  headers.set('Accept', 'application/json')
  // Con FormData (subida de una foto) NO se pone Content-Type: el navegador lo arma solo, con el "boundary" que separa las partes.
  if (options.body && !(options.body instanceof FormData) && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json')
  }
  if (token) {
    headers.set('Authorization', `Bearer ${token}`)
  }
  if (!headers.has('X-Correlation-ID')) {
    headers.set('X-Correlation-ID', newCorrelationId())
  }

  // Tiempo límite (#199): se corta el pedido completo, también si se cuelga mientras llega el cuerpo.
  // Si quien llama pasa su propia señal de cancelación, también se respeta.
  const control = new AbortController()
  let tiempoAgotado = false
  const temporizador = setTimeout(() => {
    tiempoAgotado = true
    control.abort()
  }, tiempoLimiteMs)
  const cancelarPorQuienLlama = () => control.abort()
  if (senalExterna?.aborted) control.abort()
  else senalExterna?.addEventListener('abort', cancelarPorQuienLlama, { once: true })

  try {
    const response = await fetch(`${API_BASE_URL}${path}`, {
      ...options,
      headers,
      signal: control.signal,
    })

    if (!response.ok) {
      // Si el servidor devuelve 401 y ya había un token activo, la sesión expiró.
      // Notificamos a AuthContext para que limpie el estado sin romper el flujo normal de errores.
      if (response.status === 401 && token && onUnauthorized) {
        onUnauthorized()
      }
      const problem = (await response
        .json()
        .catch(() => ({}))) as ProblemDetails
      throw new ApiError(response.status, {
        ...problem,
        correlationId:
          problem.correlationId ??
          response.headers.get('X-Correlation-ID') ??
          undefined,
      })
    }

    if (response.status === 204) {
      return { datos: undefined, encabezados: response.headers }
    }
    // Leemos como texto y después intentamos JSON: algunos endpoints (ej. POST /api/obras)
    // devuelven un string plano y response.json() fallaría con ese cuerpo.
    const texto = await response.text()
    if (!texto) {
      return { datos: undefined, encabezados: response.headers }
    }
    try {
      return { datos: JSON.parse(texto), encabezados: response.headers }
    } catch {
      return { datos: texto, encabezados: response.headers }
    }
  } catch (err) {
    if (tiempoAgotado) throw errorDeTiempoAgotado()
    throw err
  } finally {
    clearTimeout(temporizador)
    senalExterna?.removeEventListener('abort', cancelarPorQuienLlama)
  }
}

export async function apiFetch<T>(
  path: string,
  options: OpcionesApi = {},
): Promise<T> {
  const { datos } = await ejecutar(path, options)
  return datos as T
}

function leerEntero(valor: string | null): number | undefined {
  if (valor === null) return undefined
  const numero = Number.parseInt(valor, 10)
  return Number.isFinite(numero) && numero >= 0 ? numero : undefined
}

function leerEnteroEstricto(valor: string | null): number | undefined {
  if (valor === null || !/^\d+$/.test(valor.trim())) return undefined
  const numero = Number(valor)
  return Number.isSafeInteger(numero) && numero >= 0 ? numero : undefined
}

export interface OpcionesPaginacionApi {
  /** Interpreta como indefinido X-Total-Count ausente, malformado o fuera del rango entero seguro. */
  exigirTotalValido?: boolean
}

/**
 * Igual que apiFetch, pero para listados paginados (#172): devuelve las filas y el total que
 * informa el servicio en X-Total-Count. Si el encabezado no está, "total" queda sin definir.
 */
export async function apiFetchPagina<T>(
  path: string,
  options: OpcionesApi = {},
  opcionesPaginacion: OpcionesPaginacionApi = {},
): Promise<PaginaDe<T>> {
  const { datos, encabezados } = await ejecutar(path, options)
  return {
    items: Array.isArray(datos) ? (datos as T[]) : [],
    total: opcionesPaginacion.exigirTotalValido
      ? leerEnteroEstricto(encabezados.get('X-Total-Count'))
      : leerEntero(encabezados.get('X-Total-Count')),
  }
}
