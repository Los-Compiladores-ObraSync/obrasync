import { apiFetch, obtenerToken } from '../api/client'

const BASE = '/api/usuarios/notificaciones'

export function soportaPush(): boolean {
  return typeof navigator !== 'undefined' && 'serviceWorker' in navigator &&
    typeof PushManager !== 'undefined' && typeof Notification !== 'undefined'
}

export function decodificarClaveVapid(clave: string): Uint8Array<ArrayBuffer> {
  const base64 = clave.replace(/-/g, '+').replace(/_/g, '/')
  const binario = atob(base64.padEnd(Math.ceil(base64.length / 4) * 4, '='))
  const resultado = new Uint8Array(new ArrayBuffer(binario.length))
  for (let indice = 0; indice < binario.length; indice++) resultado[indice] = binario.charCodeAt(indice)
  return resultado
}

const ESPERA_MAXIMA_SERVICE_WORKER_MS = 8000

/**
 * `navigator.serviceWorker.ready` no falla nunca: si no hay service worker registrado
 * (por ejemplo con `npm run dev`), la promesa queda esperando para siempre.
 * Por eso la competimos contra un temporizador y fallamos con un mensaje claro.
 */
async function esperarServiceWorker(): Promise<ServiceWorkerRegistration> {
  let temporizador: ReturnType<typeof setTimeout> | undefined
  const vencimiento = new Promise<never>((_, rechazar) => {
    temporizador = setTimeout(
      () => rechazar(new Error('El servicio de avisos todavía no está listo. Recargá la página e intentá de nuevo.')),
      ESPERA_MAXIMA_SERVICE_WORKER_MS,
    )
  })
  try {
    return await Promise.race([navigator.serviceWorker.ready, vencimiento])
  } finally {
    clearTimeout(temporizador)
  }
}

export async function leerSuscripcion(): Promise<PushSubscription | null> {
  if (!soportaPush()) return null
  try {
    const registro = await esperarServiceWorker()
    return await registro.pushManager.getSubscription()
  } catch {
    return null // sin service worker listo no hay suscripción que leer
  }
}

function camposSuscripcion(suscripcion: PushSubscription) {
  const p256dh = suscripcion.getKey('p256dh')
  const auth = suscripcion.getKey('auth')
  if (!p256dh || !auth) throw new Error('La suscripción del navegador está incompleta.')
  const codificar = (buffer: ArrayBuffer) =>
    btoa(String.fromCharCode(...new Uint8Array(buffer))).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
  return { endpoint: suscripcion.endpoint, p256dh: codificar(p256dh), auth: codificar(auth), navegador: navigator.userAgent.slice(0, 200) }
}

export async function activarPush(): Promise<void> {
  if (!soportaPush()) throw new Error('Este navegador no admite notificaciones push.')
  if (Notification.permission === 'denied') throw new Error('El permiso está bloqueado en la configuración del navegador.')
  const permiso = Notification.permission === 'granted' ? 'granted' : await Notification.requestPermission()
  if (permiso !== 'granted') throw new Error('No se concedió el permiso para mostrar notificaciones.')
  const registro = await esperarServiceWorker()
  let suscripcion = await registro.pushManager.getSubscription()
  if (!suscripcion) {
    const { publicKey } = await apiFetch<{ publicKey: string }>(`${BASE}/vapid-public-key`)
    suscripcion = await registro.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: decodificarClaveVapid(publicKey) })
  }
  try {
    await apiFetch(`${BASE}/suscripciones`, { method: 'POST', body: JSON.stringify(camposSuscripcion(suscripcion)) })
  } catch (error) {
    await suscripcion.unsubscribe().catch(() => undefined)
    throw error
  }
}

export async function desactivarPush(): Promise<void> {
  const suscripcion = await leerSuscripcion()
  if (!suscripcion) return
  const resultados = await Promise.allSettled([
    apiFetch(`${BASE}/suscripciones?endpoint=${encodeURIComponent(suscripcion.endpoint)}`, { method: 'DELETE' }),
    suscripcion.unsubscribe(),
  ])
  if (resultados.some((resultado) => resultado.status === 'rejected')) {
    throw new Error('No se pudo completar la desactivación en el servidor y este dispositivo.')
  }
}

/** Limpieza de mejor esfuerzo: el logout continúa sin esperar a la red. */
export function limpiarPushAlCerrarSesion(): void {
  const tokenActual = obtenerToken()
  void (async () => {
    try {
      const suscripcion = await leerSuscripcion()
      if (!suscripcion) return
      const eliminarRemoto = apiFetch(`${BASE}/suscripciones?endpoint=${encodeURIComponent(suscripcion.endpoint)}`, {
        method: 'DELETE', suprimirCierrePorUnauthorized: true, tokenParaLimpieza: tokenActual ?? undefined, tiempoLimiteMs: 3000,
      }).catch(() => undefined)
      const cancelarLocal = suscripcion.unsubscribe().catch(() => undefined)
      await Promise.all([eliminarRemoto, cancelarLocal])
    } catch { /* La sesión debe cerrarse aunque el navegador no permita limpiar push. */ }
  })()
}

export function mensajeErrorPush(error: unknown): string {
  const status = (error as { status?: number })?.status
  if (status === 503) return 'Los avisos no están disponibles por el momento. Intentá más tarde.'
  if (status === 401) return 'Tu sesión venció. Volvé a iniciar sesión e intentá nuevamente.'
  return error instanceof Error ? error.message : 'No se pudo actualizar la configuración de avisos.'
}

export function iosInstalacionRequerida(): boolean {
  if (typeof navigator === 'undefined' || !/iPhone|iPad|iPod/i.test(navigator.userAgent)) return false
  const instalada = window.matchMedia?.('(display-mode: standalone)').matches ||
    (navigator as Navigator & { standalone?: boolean }).standalone === true
  return !instalada
}
