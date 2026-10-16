import { ApiError, type ProblemDetails } from './ApiError'
import { API_BASE_URL } from './config'

let token: string | null = null

export function setToken(value: string | null): void {
  token = value
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
  return (await response.json()) as T
}
