import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { delay, http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it } from 'vitest'
import {
  ORDEN_COMPRA_SIN_DESPACHAR_1_ID,
  reiniciarOrdenesCompraMock,
} from '../mocks/handlers'
import { server } from '../mocks/server'
import { OrdenesCompraPage } from './OrdenesCompraPage'

const RUTA_LISTA = '*/api/ordenes-compra'
const RUTA_DESPACHAR = '*/api/ordenes-compra/:id/despachar'
const CODIGO_1 = 'C1C2C3C4' // las tres órdenes del mock empiezan igual: se distinguen por el final del id

function renderPagina() {
  return render(
    <MemoryRouter>
      <OrdenesCompraPage />
    </MemoryRouter>,
  )
}

/** Las tarjetas de orden, en el orden en que se ven. */
async function tarjetas() {
  await screen.findByRole('list')
  return screen.getAllByRole('listitem')
}

function problema(status: number, detail: string) {
  return HttpResponse.json({ title: 'Error', status, detail }, { status })
}

describe('OrdenesCompraPage (#205)', () => {
  beforeEach(() => reiniciarOrdenesCompraMock())

  it('lista las órdenes con la obra y su estado: primero las que faltan despachar', async () => {
    renderPagina()

    const filas = await tarjetas()
    expect(filas).toHaveLength(3)
    // Las dos sin despachar (la más nueva primero) y al final la ya despachada.
    expect(within(filas[0]).getByText('Sin despachar')).toBeInTheDocument()
    expect(within(filas[0]).getByText('Emitida el 18 de noviembre de 2026')).toBeInTheDocument()
    expect(within(filas[1]).getByText('Emitida el 17 de noviembre de 2026')).toBeInTheDocument()
    expect(within(filas[2]).getByText('Despachada')).toBeInTheDocument()
    expect(within(filas[2]).getByText('Despachada el 13 de noviembre de 2026')).toBeInTheDocument()
    expect(within(filas[0]).getByText('Torre Alvear')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: '← Volver a Gestión y Compras' })).toHaveAttribute(
      'href',
      '/compras',
    )
  })

  it('solo las órdenes sin despachar tienen el botón "Marcar como despachada"', async () => {
    renderPagina()

    const filas = await tarjetas()
    expect(within(filas[0]).getByRole('button', { name: /^Marcar como despachada/ })).toBeInTheDocument()
    expect(within(filas[1]).getByRole('button', { name: /^Marcar como despachada/ })).toBeInTheDocument()
    expect(within(filas[2]).queryByRole('button')).not.toBeInTheDocument()
  })

  it('pide confirmación y, al confirmar, despacha la orden y lo avisa', async () => {
    const user = userEvent.setup()
    renderPagina()

    const filas = await tarjetas()
    await user.click(within(filas[0]).getByRole('button', { name: /^Marcar como despachada/ }))
    // Antes de confirmar todavía no se despachó nada y se avisa que no se puede deshacer.
    expect(within(filas[0]).getByText(/No se puede deshacer/)).toBeInTheDocument()
    expect(within(filas[0]).getByText('Sin despachar')).toBeInTheDocument()

    await user.click(within(filas[0]).getByRole('button', { name: 'Sí, marcar como despachada' }))

    expect(await screen.findByRole('status')).toHaveTextContent(
      `La orden ${CODIGO_1} quedó despachada. Se avisó a Recepción`,
    )
    // La orden pasa al final del listado como despachada y ya no ofrece el botón.
    const despues = await tarjetas()
    expect(despues).toHaveLength(3)
    expect(screen.getAllByText('Despachada')).toHaveLength(2)
    expect(screen.getAllByRole('button', { name: /^Marcar como despachada/ })).toHaveLength(1)
  })

  it('"Cancelar" no despacha nada y vuelve al botón', async () => {
    let pedidos = 0
    server.use(
      http.post(RUTA_DESPACHAR, () => {
        pedidos += 1
        return HttpResponse.json({})
      }),
    )
    const user = userEvent.setup()
    renderPagina()

    const filas = await tarjetas()
    await user.click(within(filas[0]).getByRole('button', { name: /^Marcar como despachada/ }))
    await user.click(within(filas[0]).getByRole('button', { name: 'Cancelar' }))

    expect(within(filas[0]).getByRole('button', { name: /^Marcar como despachada/ })).toBeInTheDocument()
    expect(within(filas[0]).queryByText(/No se puede deshacer/)).not.toBeInTheDocument()
    expect(pedidos).toBe(0)
  })

  it('un doble toque en "Sí, marcar como despachada" manda un solo pedido', async () => {
    let pedidos = 0
    server.use(
      http.post(RUTA_DESPACHAR, async ({ params }) => {
        pedidos += 1
        await delay(50)
        return HttpResponse.json({
          id: params.id,
          estado: 'Despachada',
          fueDespachada: true,
          fechaDespacho: '2026-11-19T12:00:00Z',
        })
      }),
    )
    const user = userEvent.setup()
    renderPagina()

    const filas = await tarjetas()
    await user.click(within(filas[0]).getByRole('button', { name: /^Marcar como despachada/ }))
    await user.dblClick(within(filas[0]).getByRole('button', { name: 'Sí, marcar como despachada' }))

    await screen.findByText(/quedó despachada/)
    expect(pedidos).toBe(1)
  })

  it('muestra "Despachando…" y bloquea los botones mientras espera la respuesta', async () => {
    server.use(
      http.post(RUTA_DESPACHAR, async () => {
        await delay(100)
        return problema(409, 'La orden de compra ya fue despachada.')
      }),
    )
    const user = userEvent.setup()
    renderPagina()

    const filas = await tarjetas()
    await user.click(within(filas[0]).getByRole('button', { name: /^Marcar como despachada/ }))
    await user.click(within(filas[0]).getByRole('button', { name: 'Sí, marcar como despachada' }))

    expect(within(filas[0]).getByRole('button', { name: 'Despachando…' })).toBeDisabled()
    expect(within(filas[0]).getByRole('button', { name: 'Cancelar' })).toBeDisabled()
    expect(within(filas[1]).getByRole('button', { name: /^Marcar como despachada/ })).toBeDisabled()
    await screen.findByRole('alert')
  })

  it('403: avisa que no tiene permiso', async () => {
    server.use(http.post(RUTA_DESPACHAR, () => problema(403, 'Prohibido.')))
    const user = userEvent.setup()
    renderPagina()

    const filas = await tarjetas()
    await user.click(within(filas[0]).getByRole('button', { name: /^Marcar como despachada/ }))
    await user.click(within(filas[0]).getByRole('button', { name: 'Sí, marcar como despachada' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'No tenés permiso para despachar órdenes de compra.',
    )
  })

  it('404: avisa que la orden ya no existe y actualiza la lista', async () => {
    const orden = {
      id: ORDEN_COMPRA_SIN_DESPACHAR_1_ID,
      licitacionId: 'b1b2c3d4-1111-0000-0000-000000000001',
      cotizacionId: 'f1f2f3f4-0000-0000-0000-000000000001',
      obraId: 'b1b2c3d4-0000-0000-0000-0000000000a1',
      proveedorId: 'a1b2c3d4-0000-0000-0000-000000000003',
      estado: 'Emitida',
      fechaEmision: '2026-11-18T12:00:00Z',
      fueDespachada: false,
      fechaDespacho: null,
    }
    // Primera carga: la orden está. Cuando se vuelve a pedir la lista ya no existe.
    server.use(
      http.get(RUTA_LISTA, () => HttpResponse.json([orden], { headers: { 'X-Total-Count': '1' } }), {
        once: true,
      }),
      http.get(RUTA_LISTA, () => HttpResponse.json([])),
      http.post(RUTA_DESPACHAR, () => problema(404, 'No se encontró la orden de compra.')),
    )
    const user = userEvent.setup()
    renderPagina()

    const filas = await tarjetas()
    await user.click(within(filas[0]).getByRole('button', { name: /^Marcar como despachada/ }))
    await user.click(within(filas[0]).getByRole('button', { name: 'Sí, marcar como despachada' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('No se encontró la orden de compra.')
    expect(await screen.findByText('Todavía no hay órdenes de compra.')).toBeInTheDocument()
  })

  it('409: avisa que ya estaba despachada y actualiza la lista', async () => {
    server.use(http.post(RUTA_DESPACHAR, () => problema(409, 'La orden de compra ya fue despachada.')))
    const user = userEvent.setup()
    renderPagina()

    const filas = await tarjetas()
    await user.click(within(filas[0]).getByRole('button', { name: /^Marcar como despachada/ }))
    await user.click(within(filas[0]).getByRole('button', { name: 'Sí, marcar como despachada' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('La orden de compra ya fue despachada.')
    // Se cerró la confirmación y la orden sigue en la lista (la lista se volvió a pedir).
    expect(screen.queryByRole('button', { name: 'Sí, marcar como despachada' })).not.toBeInTheDocument()
    expect(await tarjetas()).toHaveLength(3)
  })

  it('si el servidor no responde, avisa y deja intentar de nuevo', async () => {
    server.use(http.post(RUTA_DESPACHAR, () => HttpResponse.error()))
    const user = userEvent.setup()
    renderPagina()

    const filas = await tarjetas()
    await user.click(within(filas[0]).getByRole('button', { name: /^Marcar como despachada/ }))
    await user.click(within(filas[0]).getByRole('button', { name: 'Sí, marcar como despachada' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'No se pudo conectar con el servidor. Intentá nuevamente.',
    )
    expect(within(filas[0]).getByRole('button', { name: /^Marcar como despachada/ })).toBeEnabled()
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
