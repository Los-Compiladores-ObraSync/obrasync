import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AuthContext, type SesionUsuario } from '../auth/sessionContext'
import { reiniciarRequisicionesMock } from '../mocks/handlers'
import { server } from '../mocks/server'
import { PendientesProvider } from './PendientesProvider'
import { usePendientes } from './usePendientes'

function Cantidad() {
  const { cantidad, refrescar } = usePendientes()
  return (
    <>
      <p data-testid="cantidad">{cantidad}</p>
      <button type="button" onClick={refrescar}>
        Refrescar
      </button>
    </>
  )
}

function renderizar(rol: string, intervaloMs?: number) {
  const usuario: SesionUsuario = {
    id: 'u-1',
    nombreCompleto: 'Persona de Prueba',
    email: 'prueba@obrasync.com',
    rol,
  }
  return render(
    <AuthContext.Provider value={{ usuario, login: () => {}, logout: () => {} }}>
      <PendientesProvider intervaloMs={intervaloMs}>
        <Cantidad />
      </PendientesProvider>
    </AuthContext.Provider>,
  )
}

/** Reemplaza GET /api/requisiciones: devuelve `lista()` y anota cada pedido. */
function simularPendientes(lista: () => unknown[]) {
  const pedidos: string[] = []
  server.use(
    http.get('*/api/requisiciones', ({ request }) => {
      pedidos.push(new URL(request.url).search)
      return HttpResponse.json(lista())
    }),
  )
  return pedidos
}

const esperar = (ms: number) => new Promise((resolver) => setTimeout(resolver, ms))

describe('PendientesProvider', () => {
  beforeEach(() => {
    reiniciarRequisicionesMock()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('el Comprador ve cuántas requisiciones pendientes hay (las 2 del mock)', async () => {
    renderizar('Comprador')

    await waitFor(() => expect(screen.getByTestId('cantidad')).toHaveTextContent('2'))
  })

  it('pide la lista filtrada por estado Pendiente', async () => {
    const pedidos = simularPendientes(() => [{}, {}, {}])
    renderizar('Admin')

    await waitFor(() => expect(screen.getByTestId('cantidad')).toHaveTextContent('3'))
    expect(pedidos[0]).toBe('?estado=Pendiente')
  })

  it.each(['Capataz', 'Proveedor'])('el %s no pide nada y ve 0', async (rol) => {
    const pedidos = simularPendientes(() => [{}, {}])
    renderizar(rol)

    await esperar(100)
    expect(pedidos).toHaveLength(0)
    expect(screen.getByTestId('cantidad')).toHaveTextContent('0')
  })

  it('refrescar() vuelve a pedir y actualiza el número', async () => {
    let hay = 2
    simularPendientes(() => Array.from({ length: hay }, () => ({})))
    const usuario = userEvent.setup()
    renderizar('Comprador')
    await waitFor(() => expect(screen.getByTestId('cantidad')).toHaveTextContent('2'))

    hay = 1
    await usuario.click(screen.getByRole('button', { name: 'Refrescar' }))

    await waitFor(() => expect(screen.getByTestId('cantidad')).toHaveTextContent('1'))
  })

  it('vuelve a pedir cada cierto tiempo', async () => {
    let hay = 2
    simularPendientes(() => Array.from({ length: hay }, () => ({})))
    renderizar('Comprador', 40)
    await waitFor(() => expect(screen.getByTestId('cantidad')).toHaveTextContent('2'))

    hay = 5

    await waitFor(() => expect(screen.getByTestId('cantidad')).toHaveTextContent('5'))
  })

  it('no pide nada mientras la pestaña está oculta y retoma al volver', async () => {
    const oculta = vi.spyOn(document, 'hidden', 'get').mockReturnValue(true)
    const pedidos = simularPendientes(() => [{}])
    renderizar('Comprador', 30)
    // El primer pedido (al montarse) sale igual: es el de "al iniciar sesión".
    await waitFor(() => expect(pedidos).toHaveLength(1))

    await esperar(150)
    expect(pedidos).toHaveLength(1)

    oculta.mockReturnValue(false)
    act(() => {
      document.dispatchEvent(new Event('visibilitychange'))
    })

    await waitFor(() => expect(pedidos.length).toBeGreaterThan(1))
  })

  it('si el servidor falla, no rompe y conserva el último número', async () => {
    let falla = false
    server.use(
      http.get('*/api/requisiciones', () =>
        falla
          ? HttpResponse.json({ status: 500 }, { status: 500 })
          : HttpResponse.json([{}, {}]),
      ),
    )
    const usuario = userEvent.setup()
    renderizar('Comprador')
    await waitFor(() => expect(screen.getByTestId('cantidad')).toHaveTextContent('2'))

    falla = true
    await usuario.click(screen.getByRole('button', { name: 'Refrescar' }))
    await esperar(100)

    expect(screen.getByTestId('cantidad')).toHaveTextContent('2')
  })
})
