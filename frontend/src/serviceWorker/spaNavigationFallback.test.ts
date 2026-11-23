import { beforeEach, describe, expect, it, vi } from 'vitest'
import { NavigationRoute } from 'workbox-routing'

const workbox = vi.hoisted(() => ({
  handler: vi.fn(),
  createHandlerBoundToURL: vi.fn(),
  registerRoute: vi.fn(),
}))

vi.mock('workbox-precaching', () => ({
  createHandlerBoundToURL: workbox.createHandlerBoundToURL,
}))

vi.mock('workbox-routing', async (importOriginal) => {
  const original = await importOriginal<typeof import('workbox-routing')>()
  return { ...original, registerRoute: workbox.registerRoute }
})

import { registrarFallbackNavegacion } from './spaNavigationFallback'

describe('fallback de navegación SPA', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    workbox.createHandlerBoundToURL.mockReturnValue(workbox.handler)
  })

  it('usa el index precargado para navegaciones internas y excluye endpoints API', () => {
    registrarFallbackNavegacion()

    expect(workbox.createHandlerBoundToURL).toHaveBeenCalledWith('index.html')
    expect(workbox.registerRoute).toHaveBeenCalledOnce()
    const ruta = workbox.registerRoute.mock.calls[0][0] as NavigationRoute
    expect(ruta).toBeInstanceOf(NavigationRoute)

    const navegar = (path: string) => ruta.match({
      event: {} as ExtendableEvent,
      url: new URL(path, 'https://obrasync.test'),
      request: { mode: 'navigate' } as Request,
      sameOrigin: true,
    })

    expect(navegar('/compras/ordenes/123')).toBe(true)
    expect(navegar('/')).toBe(true)
    expect(navegar('/api/usuarios/notificaciones/vapid-public-key')).toBe(false)
    expect(navegar('/api')).toBe(false)
    expect(ruta.match({
      event: {} as ExtendableEvent,
      url: new URL('/compras/ordenes/123', 'https://obrasync.test'),
      request: { mode: 'cors' } as Request,
      sameOrigin: true,
    })).toBe(false)
  })
})
