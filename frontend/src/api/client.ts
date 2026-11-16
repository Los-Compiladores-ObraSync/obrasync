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

interface RespuestaCruda {
  datos: unknown
  encabezados: Headers
}

async function ejecutar(
  path: string,
  options: RequestInit,
): Promise<RespuestaCruda> {
  const headers = new Headers(options.headers)
  headers.set('Accept', 'application/json')
  if (options.body && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json')
  }
  if (token) {
    headers.set('Authorization', `Bearer ${token}`)
  }
  if (!headers.has('X-Correlation-ID')) {
    headers.set('X-Correlation-ID', newCorrelationId())
  }

  const response = await fetch(`${API_BASE_URL}${path}`, {
    ...options,
    headers,
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
}

export async function apiFetch<T>(
  path: string,
  options: RequestInit = {},
): Promise<T> {
  const { datos } = await ejecutar(path, options)
  return datos as T
}

function leerEntero(valor: string | null): number | undefined {
  if (valor === null) return undefined
  const numero = Number.parseInt(valor, 10)
  return Number.isFinite(numero) && numero >= 0 ? numero : undefined
}

/**
 * Igual que apiFetch, pero para listados paginados (#172): devuelve las filas y el total que
 * informa el servicio en X-Total-Count. Si el encabezado no está, "total" queda sin definir.
 */
export async function apiFetchPagina<T>(
  path: string,
  options: RequestInit = {},
): Promise<PaginaDe<T>> {
  const { datos, encabezados } = await ejecutar(path, options)
  return {
    items: Array.isArray(datos) ? (datos as T[]) : [],
    total: leerEntero(encabezados.get('X-Total-Count')),
  }
}
