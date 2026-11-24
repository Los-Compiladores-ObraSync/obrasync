/// <reference lib="webworker" />
import { cleanupOutdatedCaches, precacheAndRoute } from 'workbox-precaching'
import { clientsClaim } from 'workbox-core'
import { registrarFallbackNavegacion } from './serviceWorker/spaNavigationFallback'

declare let self: ServiceWorkerGlobalScope & { __WB_MANIFEST: Array<unknown> }
precacheAndRoute(self.__WB_MANIFEST)
registrarFallbackNavegacion()
cleanupOutdatedCaches()
clientsClaim()
self.skipWaiting()
