import { afterEach, describe, expect, it, vi } from 'vitest'
import { hayVentanaVisible } from './visibleWindows'

afterEach(() => vi.useRealTimers())

function ventanaVisible(alResponder: (puerto: MessagePort) => void) {
  return {
    visibilityState: 'visible',
    postMessage: vi.fn((_mensaje: unknown, transfer: Transferable[]) => {
      alResponder(transfer[0] as MessagePort)
    }),
  }
}

describe('respaldo de notificación ante errores de ventanas', () => {
  it('continúa con notificación del sistema cuando matchAll rechaza', async () => {
    await expect(hayVentanaVisible(() => Promise.reject(new Error('matchAll')))).resolves.toBe(false)
  })

  it('trata postMessage rechazado como falta de confirmación visible', async () => {
    const ventana = {
      visibilityState: 'visible',
      postMessage: vi.fn(() => { throw new Error('postMessage') }),
    }
    await expect(hayVentanaVisible(async () => [ventana])).resolves.toBe(false)
  })

  it('con varias ventanas, una que falla no impide notificar si otra confirma visibilidad', async () => {
    const falla = {
      visibilityState: 'visible',
      postMessage: vi.fn(() => { throw new Error('ventana cerrada') }),
    }
    const confirma = ventanaVisible((puerto) => puerto.postMessage('visible'))
    await expect(hayVentanaVisible(async () => [falla, confirma])).resolves.toBe(true)
    expect(falla.postMessage).toHaveBeenCalledOnce()
    expect(confirma.postMessage).toHaveBeenCalledOnce()
  })

  it('confirma la ventana visible para evitar duplicar la notificación del sistema', async () => {
    const ventana = ventanaVisible((puerto) => puerto.postMessage('visible'))
    await expect(hayVentanaVisible(async () => [ventana])).resolves.toBe(true)
  })

  it('muestra respaldo si no hay ventanas visibles', async () => {
    const oculta = { visibilityState: 'hidden', postMessage: vi.fn() }
    await expect(hayVentanaVisible(async () => [oculta])).resolves.toBe(false)
    expect(oculta.postMessage).not.toHaveBeenCalled()
  })
})
