import { ApiError, type ProblemDetails } from './ApiError'
import { API_BASE_URL } from './config'

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

export async function apiFetch<T>(
  path: string,
  options: RequestInit = {},
): Promise<T> {
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
    return undefined as T
  }
  // Leemos como texto y después intentamos JSON: algunos endpoints (ej. POST /api/obras)
  // devuelven un string plano y response.json() fallaría con ese cuerpo.
  const texto = await response.text()
  if (!texto) {
    return undefined as T
  }
  try {
    return JSON.parse(texto) as T
  } catch {
    return texto as T
  }
}
