/// <reference lib="webworker" />
import { cleanupOutdatedCaches, precacheAndRoute } from 'workbox-precaching'
import { clientsClaim } from 'workbox-core'
import { registrarFallbackNavegacion } from './serviceWorker/spaNavigationFallback'
import { hayVentanaVisible } from './serviceWorker/visibleWindows'

declare let self: ServiceWorkerGlobalScope & { __WB_MANIFEST: Array<unknown> }
precacheAndRoute(self.__WB_MANIFEST)
registrarFallbackNavegacion()
cleanupOutdatedCaches()
clientsClaim()
self.skipWaiting()

type PayloadPush = { titulo?: unknown; cuerpo?: unknown; ruta?: unknown; tipo?: unknown; timestampUtc?: unknown }
function rutaSegura(valor: unknown): string {
  if (typeof valor !== 'string' || !valor.startsWith('/') || valor.startsWith('//') || valor.includes('\\')) return '/'
  try {
    const url = new URL(valor, self.location.origin)
    return url.origin === self.location.origin ? `${url.pathname}${url.search}${url.hash}` : '/'
  } catch { return '/' }
}

self.addEventListener('push', (evento: PushEvent) => {
  evento.waitUntil((async () => {
    let payload: PayloadPush = {}
    try { payload = evento.data?.json() as PayloadPush ?? {} } catch { /* El payload mal formado conserva un aviso de respaldo. */ }
    if (await hayVentanaVisible(() => self.clients.matchAll({ type: 'window', includeUncontrolled: true }))) return
    const titulo = typeof payload.titulo === 'string' && payload.titulo.trim() ? payload.titulo : 'ObraSync'
    const cuerpo = typeof payload.cuerpo === 'string' ? payload.cuerpo : 'Tenés un aviso nuevo.'
    await self.registration.showNotification(titulo, { body: cuerpo, data: { ruta: rutaSegura(payload.ruta), tipo: payload.tipo } })
  })())
})

self.addEventListener('notificationclick', (evento: NotificationEvent) => {
  evento.notification.close()
  const ruta = rutaSegura((evento.notification.data as { ruta?: unknown } | undefined)?.ruta)
  evento.waitUntil((async () => {
    const ventanas = await self.clients.matchAll({ type: 'window', includeUncontrolled: true })
    const existente = ventanas[0]
    if (existente) {
      await existente.focus()
      existente.postMessage({ type: 'OBRASYNC_PUSH_NAVIGATE', ruta })
      return
    }
    await self.clients.openWindow(new URL(ruta, self.location.origin).href)
  })())
})
