import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { setToken } from '../api/client'
import { AvisosEnEsteCelular } from './AvisosEnEsteCelular'
import { activarPush, decodificarClaveVapid, desactivarPush, leerSuscripcion, limpiarPushAlCerrarSesion } from './pushService'

afterEach(() => { cleanup(); setToken(null); vi.unstubAllGlobals(); vi.restoreAllMocks() })

describe('notificaciones push', () => {
  it('decodifica claves VAPID base64url con padding omitido', () => {
    expect([...decodificarClaveVapid('-_8')]).toEqual([251, 255])
    expect([...decodificarClaveVapid('AQID')]).toEqual([1, 2, 3])
  })

  it('no solicita permisos al mostrar la interfaz', () => {
    render(<AvisosEnEsteCelular />)
    expect(screen.getByText(/no admite notificaciones push/i)).toBeInTheDocument()
  })

  it('solicita permiso al activar y registra exactamente endpoint, p256dh y auth', async () => {
    const requestPermission = vi.fn().mockResolvedValue('granted')
    class FakePushManager {}
    vi.stubGlobal('PushManager', FakePushManager)
    vi.stubGlobal('Notification', { permission: 'default', requestPermission })
    const pushSubscription = {
      endpoint: 'https://push.test/abc',
      getKey: (key: string) => key === 'p256dh' ? Uint8Array.from([1, 2]).buffer : Uint8Array.from([3]).buffer,
      unsubscribe: vi.fn().mockResolvedValue(true),
    }
    const getSubscription = vi.fn().mockResolvedValue(null)
    const subscribe = vi.fn().mockResolvedValue(pushSubscription)
    Object.defineProperty(navigator, 'serviceWorker', { configurable: true, value: { ready: Promise.resolve({ pushManager: { getSubscription, subscribe } }) } })
    const llamadas: Array<{ url: string; init?: RequestInit }> = []
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      llamadas.push({ url: String(input), init })
      return new Response(JSON.stringify({ publicKey: 'AQID' }), { status: 200, headers: { 'Content-Type': 'application/json' } })
    }))

    await activarPush()

    expect(requestPermission).toHaveBeenCalledOnce()
    expect(subscribe).toHaveBeenCalledWith({ userVisibleOnly: true, applicationServerKey: decodificarClaveVapid('AQID') })
    expect(llamadas.map(({ url }) => url)).toEqual(expect.arrayContaining([expect.stringContaining('/vapid-public-key'), expect.stringContaining('/suscripciones')]))
    const body = JSON.parse(String(llamadas.find(({ init }) => init?.method === 'POST')?.init?.body)) as Record<string, unknown>
    expect(Object.keys(body).sort()).toEqual(['auth', 'endpoint', 'navegador', 'p256dh'])
    expect(body).toMatchObject({ endpoint: 'https://push.test/abc', p256dh: 'AQI', auth: 'Aw' })
  })

  it('elimina por endpoint codificado y aun así intenta unsubscribe local si falla el backend', async () => {
    class FakePushManager {}
    vi.stubGlobal('PushManager', FakePushManager)
    vi.stubGlobal('Notification', { permission: 'granted', requestPermission: vi.fn() })
    const unsubscribe = vi.fn().mockRejectedValue(new Error('fallo local'))
    const suscripcion = { endpoint: 'https://push.test/recurso?a=1&b=2', unsubscribe } as unknown as PushSubscription
    Object.defineProperty(navigator, 'serviceWorker', { configurable: true, value: { ready: Promise.resolve({ pushManager: { getSubscription: vi.fn().mockResolvedValue(suscripcion) } }) } })
    const fetchMock = vi.fn().mockResolvedValue(new Response('', { status: 503 }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(desactivarPush()).rejects.toThrow(/desactivación/i)
    const url = new URL(String(fetchMock.mock.calls[0][0]))
    expect(url.pathname).toBe('/api/usuarios/notificaciones/suscripciones')
    expect(url.searchParams.get('endpoint')).toBe(suscripcion.endpoint)
    expect(unsubscribe).toHaveBeenCalledOnce()
  })

  it('el logout intenta el DELETE con el token capturado sin bloquear el cierre', async () => {
    class FakePushManager {}
    vi.stubGlobal('PushManager', FakePushManager)
    vi.stubGlobal('Notification', { permission: 'granted' })
    const unsubscribe = vi.fn().mockResolvedValue(true)
    Object.defineProperty(navigator, 'serviceWorker', { configurable: true, value: { ready: Promise.resolve({ pushManager: { getSubscription: vi.fn().mockResolvedValue({ endpoint: 'https://push.test/logout', unsubscribe }) } }) } })
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)
    setToken('token-antes-del-logout')

    limpiarPushAlCerrarSesion()

    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledOnce())
    expect(new Headers(fetchMock.mock.calls[0][1]?.headers).get('Authorization')).toBe('Bearer token-antes-del-logout')
    expect(unsubscribe).toHaveBeenCalledOnce()
  })

  it('si el service worker nunca queda listo, activarPush falla con un mensaje en vez de colgarse', async () => {
    vi.useFakeTimers()
    try {
      class FakePushManager {}
      vi.stubGlobal('PushManager', FakePushManager)
      vi.stubGlobal('Notification', { permission: 'granted', requestPermission: vi.fn() })
      Object.defineProperty(navigator, 'serviceWorker', { configurable: true, value: { ready: new Promise(() => undefined) } })

      const comprobacion = expect(activarPush()).rejects.toThrow(/servicio de avisos/i)
      await vi.advanceTimersByTimeAsync(8000)
      await comprobacion

      const lectura = leerSuscripcion()
      await vi.advanceTimersByTimeAsync(8000)
      await expect(lectura).resolves.toBeNull()
    } finally {
      vi.useRealTimers()
    }
  })
})
