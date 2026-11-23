import { NavigationRoute, registerRoute } from 'workbox-routing'
import { createHandlerBoundToURL } from 'workbox-precaching'

/** Resuelve las rutas de React Router con el index precargado; la API queda fuera del fallback SPA. */
export function registrarFallbackNavegacion(): void {
  const handler = createHandlerBoundToURL('index.html')
  const ruta = new NavigationRoute(handler, {
    denylist: [/^\/api(?:\/|$)/],
  })
  registerRoute(ruta)
}
