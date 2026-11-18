import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AuthContext, type SesionUsuario } from '../auth/sessionContext'
import { MOCK_TOKEN_ADMIN, MOCK_TOKEN_COMPRADOR, reiniciarPresupuestoMock } from '../mocks/handlers'
import { setToken } from '../api/client'
import * as modoSimulado from '../mocks/modoSimulado'
import { server } from '../mocks/server'
import { BloqueadasProvider } from './BloqueadasProvider'
import { useBloqueadas } from './useBloqueadas'

function Cantidad() {
  const { cantidad, bloqueadas, cargado, huboError, refrescar } = useBloqueadas()
  return (
    <>
      <p data-testid="cantidad">{cantidad}</p>
      <p data-testid="lista">{bloqueadas.length}</p>
      <p data-testid="cargado">{String(cargado)}</p>
      <p data-testid="error">{String(huboError)}</p>
      <button type="button" onClick={refrescar}>
        Refrescar
      </button>
    </>
  )
}

function renderizar(rol: string, intervaloMs?: number) {
  setToken(rol === 'Admin' ? MOCK_TOKEN_ADMIN : MOCK_TOKEN_COMPRADOR)
  const usuario: SesionUsuario = {
    id: 'u-1',
    nombreCompleto: 'Persona de Prueba',
    email: 'prueba@obrasync.com',
    rol,
  }
  return render(
    <AuthContext.Provider value={{ usuario, login: () => {}, logout: () => {} }}>
      <BloqueadasProvider intervaloMs={intervaloMs}>
        <Cantidad />
      </BloqueadasProvider>
    </AuthContext.Provider>,
  )
}

const esperar = (ms: number) => new Promise((resolver) => setTimeout(resolver, ms))
const respuestaLista = (items: unknown[]) =>
  HttpResponse.json(items, { headers: { 'X-Total-Count': String(items.length) } })

describe('BloqueadasProvider', () => {
  beforeEach(() => {
    reiniciarPresupuestoMock()
    vi.restoreAllMocks()
    vi.spyOn(modoSimulado, 'estaEnModoSimulado').mockReturnValue(true)
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('consulta al montarse para Comprador y muestra las órdenes bloqueadas', async () => {
    renderizar('Comprador')

    await waitFor(() => expect(screen.getByTestId('cantidad')).toHaveTextContent('2'))
    expect(screen.getByTestId('lista')).toHaveTextContent('2')
    expect(screen.getByTestId('cargado')).toHaveTextContent('true')
    expect(screen.getByTestId('error')).toHaveTextContent('false')
  })

  it('consulta al montarse para Admin y muestra las órdenes bloqueadas', async () => {
    renderizar('Admin')

    await waitFor(() => expect(screen.getByTestId('cantidad')).toHaveTextContent('2'))
    expect(screen.getByTestId('lista')).toHaveTextContent('2')
    expect(screen.getByTestId('cargado')).toHaveTextContent('true')
  })

  it.each(['Capataz', 'Proveedor'])('el rol %s no consulta y ve 0', async (rol) => {
    let llamadas = 0
    server.use(
      http.get('*/api/recepciones/ordenes-bloqueadas', () => {
        llamadas += 1
        return respuestaLista([])
      }),
    )

    renderizar(rol)

    await esperar(100)
    expect(llamadas).toBe(0)
    expect(screen.getByTestId('cantidad')).toHaveTextContent('0')
    expect(screen.getByTestId('lista')).toHaveTextContent('0')
    expect(screen.getByTestId('cargado')).toHaveTextContent('true')
  })

  it('actualiza por intervalo cada cierto tiempo', async () => {
    let llamadas = 0
    server.use(
      http.get('*/api/recepciones/ordenes-bloqueadas', () => {
        llamadas += 1
        if (llamadas === 1) {
          return respuestaLista([{ ordenCompraId: 'oc-1' }])
        }
        return respuestaLista([{ ordenCompraId: 'oc-1' }, { ordenCompraId: 'oc-2' }, { ordenCompraId: 'oc-3' }])
      }),
    )

    renderizar('Comprador', 30)
    await waitFor(() => expect(screen.getByTestId('cantidad')).toHaveTextContent('1'))

    await waitFor(() => expect(screen.getByTestId('cantidad')).toHaveTextContent('3'))
  })

  it('no consulta periódicamente con la pestaña oculta y retoma al recuperar visibilidad', async () => {
    const oculta = vi.spyOn(document, 'hidden', 'get').mockReturnValue(true)
    let llamadas = 0
    server.use(
      http.get('*/api/recepciones/ordenes-bloqueadas', () => {
        llamadas += 1
        return respuestaLista([{ ordenCompraId: 'oc-1' }])
      }),
    )

    renderizar('Comprador', 30)
    await waitFor(() => expect(llamadas).toBe(1))

    await esperar(120)
    expect(llamadas).toBe(1)

    oculta.mockReturnValue(false)
    act(() => {
      document.dispatchEvent(new Event('visibilitychange'))
    })

    await waitFor(() => expect(llamadas).toBeGreaterThan(1))
  })

  it('conserva el último dato disponible ante errores de red', async () => {
    let falla = false
    server.use(
      http.get('*/api/recepciones/ordenes-bloqueadas', () => {
        if (falla) {
          return HttpResponse.json({ status: 500 }, { status: 500 })
        }
        return respuestaLista([{ ordenCompraId: 'oc-1' }, { ordenCompraId: 'oc-2' }])
      }),
    )

    const usuario = userEvent.setup()
    renderizar('Comprador')
    await waitFor(() => expect(screen.getByTestId('cantidad')).toHaveTextContent('2'))

    falla = true
    await usuario.click(screen.getByRole('button', { name: 'Refrescar' }))
    await esperar(100)

    expect(screen.getByTestId('cantidad')).toHaveTextContent('2')
    expect(screen.getByTestId('error')).toHaveTextContent('true')
  })

  it('no actualiza estado después del desmontaje si la petición estaba en vuelo', async () => {
    server.use(
      http.get('*/api/recepciones/ordenes-bloqueadas', async () => {
        await esperar(100)
        return respuestaLista([{ ordenCompraId: 'oc-1' }])
      }),
    )

    const { unmount } = renderizar('Comprador')
    unmount()

    await esperar(150)
  })

  it('consulta el endpoint cuando no está en modo simulado', async () => {
    vi.spyOn(modoSimulado, 'estaEnModoSimulado').mockReturnValue(false)
    let llamadas = 0
    server.use(
      http.get('*/api/recepciones/ordenes-bloqueadas', () => {
        llamadas += 1
        return respuestaLista([{ ordenCompraId: 'oc-real' }])
      }),
    )

    renderizar('Admin')

    await esperar(100)
    expect(llamadas).toBe(1)
    expect(screen.getByTestId('cantidad')).toHaveTextContent('1')
    expect(screen.getByTestId('lista')).toHaveTextContent('1')
  })
})
