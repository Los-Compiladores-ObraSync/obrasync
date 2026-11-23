import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { reiniciarOrdenesCompraMock } from '../mocks/handlers'
import { server } from '../mocks/server'
import { OrdenesCompraPage } from './OrdenesCompraPage'

// Estas pruebas cubren el listado y la navegación; el chat de órdenes tiene su propia suite.
vi.mock('../chat/ChatOrden', () => ({ ChatOrden: () => null }))
vi.mock('../chat/useChatEnLista', () => ({
  useChatEnLista: () => ({
    problemaDe: () => ({ abierto: false, motivo: null }),
    noLeidosDe: () => 0,
    marcarLeida: () => {},
    cambiarProblema: () => {},
  }),
}))

const RUTA_LISTA = '*/api/ordenes-compra'

function renderPagina(initialEntry = '/compras/ordenes') {
  return render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <OrdenesCompraPage />
    </MemoryRouter>,
  )
}

/** Las tarjetas de orden, en el orden en que se ven. */
async function tarjetas() {
  await screen.findByRole('list')
  return screen.getAllByRole('listitem')
}

describe('OrdenesCompraPage (#205, #223)', () => {
  beforeEach(() => reiniciarOrdenesCompraMock())

  it('solo las órdenes despachadas se pueden calificar (se califica a quien ya cumplió)', async () => {
    renderPagina()
    await tarjetas()

    // En el mock hay una sola orden despachada y dos sin despachar.
    expect(screen.getAllByRole('button', { name: 'Guardar calificación' })).toHaveLength(1)
  })

  it('lista las órdenes con la obra y su estado: primero las que faltan despachar', async () => {
    renderPagina()

    const filas = await tarjetas()
    expect(filas).toHaveLength(3)
    // Las dos sin despachar (la más nueva primero) y al final la ya despachada.
    expect(within(filas[0]).getByText('Sin despachar')).toBeInTheDocument()
    expect(within(filas[0]).getByText('Emitida el 18 de noviembre de 2026')).toBeInTheDocument()
    expect(within(filas[1]).getByText('Emitida el 17 de noviembre de 2026')).toBeInTheDocument()
    expect(within(filas[2]).getByText('Despachada')).toBeInTheDocument()
    expect(within(filas[0]).getByText('Torre Alvear')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: '← Volver al inicio' })).toHaveAttribute('href', '/')
  })

  it('Compras no puede despachar: ninguna orden tiene botón y las pendientes dicen que espera al proveedor (#223)', async () => {
    renderPagina()

    const filas = await tarjetas()
    expect(screen.queryByRole('button', { name: /despachada/i })).not.toBeInTheDocument()
    expect(within(filas[0]).getByText('Esperando que el proveedor la despache.')).toBeInTheDocument()
    expect(within(filas[1]).getByText('Esperando que el proveedor la despache.')).toBeInTheDocument()
    expect(within(filas[2]).queryByText('Esperando que el proveedor la despache.')).not.toBeInTheDocument()
  })

  it('la orden despachada muestra cuándo se despachó, cuándo llega y la observación (#223)', async () => {
    renderPagina()

    const filas = await tarjetas()
    expect(within(filas[2]).getByText('Despachada el 13 de noviembre de 2026 por el proveedor')).toBeInTheDocument()
    expect(within(filas[2]).getByText('Llega el 20 de noviembre de 2026')).toBeInTheDocument()
    expect(within(filas[2]).getByText('Observación: Sale en camión por la mañana')).toBeInTheDocument()
    expect(within(filas[2]).getByRole('link', { name: 'Ir a Recepción' })).toHaveAttribute('href', '/compras/recepcion')
  })

  it('abre y señala la orden indicada por identificador', async () => {
    renderPagina('/compras/ordenes?ordenCompraId=c1c2c3c4-0000-0000-0000-000000000002')
    const filas = await tarjetas()
    expect(within(filas[1]).getByRole('status')).toHaveTextContent('Esta es la orden generada por la adjudicación.')
    expect(filas[1]).toHaveAttribute('id', 'orden-c1c2c3c4-0000-0000-0000-000000000002')
  })

  it('si falla la carga, avisa y "Reintentar" vuelve a pedir la lista', async () => {
    server.use(http.get(RUTA_LISTA, () => HttpResponse.error(), { once: true }))
    const user = userEvent.setup()
    renderPagina()

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'No se pudieron cargar las órdenes de compra. Intentá nuevamente.',
    )
    await user.click(screen.getByRole('button', { name: 'Reintentar' }))

    expect(await tarjetas()).toHaveLength(3)
  })

  it('si todavía no hay órdenes, lo dice', async () => {
    server.use(http.get(RUTA_LISTA, () => HttpResponse.json([])))
    renderPagina()

    expect(await screen.findByText('Todavía no hay órdenes de compra.')).toBeInTheDocument()
    expect(screen.queryByRole('list')).not.toBeInTheDocument()
  })
})
