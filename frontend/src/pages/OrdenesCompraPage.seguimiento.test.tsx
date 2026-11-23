import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it } from 'vitest'
import { AuthContext } from '../auth/sessionContext'
import { reiniciarOrdenesCompraMock } from '../mocks/handlers'
import { server } from '../mocks/server'
import { OrdenesCompraPage } from './OrdenesCompraPage'

// Seguimiento de la entrega y marca de "Entrega con problema" para Compras (#224).
const RUTA_LISTA = '*/api/ordenes-compra'
const RUTA_SEGUIMIENTO = '*/api/recepciones/ordenes/:ordenCompraId'

function renderPagina() {
  return render(
    <AuthContext.Provider value={{ usuario: { id: 'comp-1', nombreCompleto: 'Comprador', email: 'c@x.com', rol: 'Comprador' }, login: () => {}, logout: () => {} }}>
      <MemoryRouter>
        <OrdenesCompraPage />
      </MemoryRouter>
    </AuthContext.Provider>,
  )
}

/** Las tarjetas de orden, en el orden en que se ven (antes de abrir ningún seguimiento). */
async function tarjetas() {
  await screen.findByRole('list')
  return screen.getAllByRole('listitem')
}

const ordenDespachada = {
  id: 'aaaaaaaa-0000',
  licitacionId: 'l1',
  cotizacionId: 'c1',
  obraId: 'o1',
  proveedorId: 'p1',
  estado: 'Despachada',
  fechaEmision: '2026-11-18T12:00:00Z',
  fueDespachada: true,
  fechaDespacho: '2026-11-19T12:00:00Z',
}

describe('OrdenesCompraPage: seguimiento de la entrega (#224)', () => {
  beforeEach(() => reiniciarOrdenesCompraMock())

  it('marca con "Entrega con problema" y el motivo la orden que lo tiene', async () => {
    server.use(http.get(RUTA_LISTA, () => HttpResponse.json([{ ...ordenDespachada, tieneProblemaAbierto: true, motivoProblemaAbierto: 'Llegó dañada' }])))
    renderPagina()

    expect(await screen.findByText('Entrega con problema: Llegó dañada')).toBeInTheDocument()
  })

  it('una orden sin problema no muestra la marca', async () => {
    renderPagina()
    await tarjetas()

    expect(screen.queryByText(/Entrega con problema/)).not.toBeInTheDocument()
  })

  it('solo las órdenes despachadas tienen seguimiento de la entrega', async () => {
    renderPagina()
    await tarjetas()

    // En el mock hay una sola orden despachada y dos sin despachar.
    expect(screen.getAllByRole('button', { name: /Ver seguimiento de la entrega/ })).toHaveLength(1)
  })

  it('al abrir el seguimiento muestra el despacho del Proveedor', async () => {
    const user = userEvent.setup()
    renderPagina()
    const filas = await tarjetas()

    await user.click(within(filas[2]).getByRole('button', { name: /Ver seguimiento de la entrega/ }))

    const linea = await screen.findByRole('list', { name: 'Línea de tiempo de la orden' })
    expect(within(linea).getByText('Despachada por el Proveedor')).toBeInTheDocument()
    expect(within(linea).getByText(/Llega el 20 de noviembre de 2026\./)).toBeInTheDocument()
  })

  it('muestra qué llegó, qué falta y el problema abierto de la orden', async () => {
    const user = userEvent.setup()
    server.use(
      http.get(RUTA_LISTA, () => HttpResponse.json([{ ...ordenDespachada, tieneProblemaAbierto: true, motivoProblemaAbierto: 'No llegó nada' }])),
      http.get(RUTA_SEGUIMIENTO, () =>
        HttpResponse.json({
          ordenCompraId: ordenDespachada.id,
          obraId: 'o1',
          fechaDespacho: '2026-11-19T12:00:00Z',
          estaCompleta: false,
          tieneProblemaAbierto: true,
          items: [{ articuloId: 'a1', descripcionArticulo: 'Cemento Portland Especial 25kg', cantidadEsperada: 80, cantidadRecibida: 0, cantidadPendiente: 80, unidadMedida: 'BOLSA' }],
          recepciones: [{ id: 'p1', estado: 4, motivoProblema: 'No llegó nada', fechaRecepcion: '2026-11-22T10:00:00Z', createdAt: '2026-11-22T10:00:00Z', fotos: [] }],
        }),
      ),
    )
    renderPagina()
    await screen.findByText('Entrega con problema: No llegó nada')

    await user.click(screen.getByRole('button', { name: /Ver seguimiento de la entrega/ }))

    expect(await screen.findByText(/Pedido 80 bolsas · Recibido 0 bolsas · Pendiente 80 bolsas/)).toBeInTheDocument()
    expect(screen.getByText('Hay un problema de entrega abierto en esta orden.')).toBeInTheDocument()
    expect(screen.getByText('Motivo: No llegó nada')).toBeInTheDocument()
  })

  it('si no se puede cargar el seguimiento avisa y deja reintentar', async () => {
    const user = userEvent.setup()
    server.use(http.get(RUTA_SEGUIMIENTO, () => HttpResponse.error()))
    renderPagina()
    const filas = await tarjetas()

    await user.click(within(filas[2]).getByRole('button', { name: /Ver seguimiento de la entrega/ }))
    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudo cargar el seguimiento de la entrega.')

    server.resetHandlers()
    await user.click(screen.getByRole('button', { name: 'Reintentar' }))

    expect(await screen.findByRole('list', { name: 'Línea de tiempo de la orden' })).toBeInTheDocument()
  })
})
