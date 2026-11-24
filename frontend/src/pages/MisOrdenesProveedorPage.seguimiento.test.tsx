import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it } from 'vitest'
import { reiniciarOrdenesCompraMock } from '../mocks/handlers'
import { server } from '../mocks/server'
import { MisOrdenesProveedorPage } from './MisOrdenesProveedorPage'

// Seguimiento de la entrega y marca de "Entrega con problema" para el Proveedor (#224).
const RUTA_LISTA = '*/api/ordenes-compra'
const RUTA_SEGUIMIENTO = '*/api/recepciones/ordenes/:ordenCompraId'

function renderPagina() {
  return render(
    <MemoryRouter>
      <MisOrdenesProveedorPage />
    </MemoryRouter>,
  )
}

/** Las tarjetas de orden, en el orden en que se ven (antes de abrir ningún seguimiento). */
async function tarjetas() {
  return screen.findAllByRole('listitem')
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

describe('MisOrdenesProveedorPage: seguimiento de la entrega (#224)', () => {
  beforeEach(() => reiniciarOrdenesCompraMock())

  it('marca con "Entrega con problema" y el motivo la orden que lo tiene', async () => {
    server.use(http.get(RUTA_LISTA, () => HttpResponse.json([{ ...ordenDespachada, tieneProblemaAbierto: true, motivoProblemaAbierto: 'Llegó incompleta' }])))
    renderPagina()

    expect(await screen.findByText('Entrega con problema: Llegó incompleta')).toBeInTheDocument()
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

  it('al abrir el seguimiento muestra el despacho que informó el Proveedor', async () => {
    const user = userEvent.setup()
    renderPagina()
    const filas = await tarjetas()

    await user.click(within(filas[2]).getByRole('button', { name: /Ver seguimiento de la entrega/ }))

    const linea = await screen.findByRole('list', { name: 'Línea de tiempo de la orden' })
    expect(within(linea).getByText('Despachada por el Proveedor')).toBeInTheDocument()
    expect(within(linea).getByText(/Llega el 20 de noviembre de 2026\./)).toBeInTheDocument()
  })

  it('muestra qué llegó, qué falta y cada entrega con su motivo', async () => {
    const user = userEvent.setup()
    server.use(
      http.get(RUTA_LISTA, () => HttpResponse.json([ordenDespachada])),
      http.get(RUTA_SEGUIMIENTO, () =>
        HttpResponse.json({
          ordenCompraId: ordenDespachada.id,
          obraId: 'o1',
          fechaDespacho: '2026-11-19T12:00:00Z',
          estaCompleta: false,
          tieneProblemaAbierto: false,
          items: [{ articuloId: 'a1', descripcionArticulo: 'Arena Fina a Granel', cantidadEsperada: 12, cantidadRecibida: 5, cantidadPendiente: 7, unidadMedida: 'M3' }],
          recepciones: [
            { id: 'p1', estado: 4, motivoProblema: 'Llegó incompleta', fechaRecepcion: '2026-11-21T10:00:00Z', createdAt: '2026-11-21T10:00:00Z', fotos: [] },
            { id: 'r1', numeroRemito: 'R-9', estado: 2, fechaRecepcion: '2026-11-22T10:00:00Z', createdAt: '2026-11-22T10:00:00Z', detalles: [] },
          ],
        }),
      ),
    )
    renderPagina()

    await user.click(await screen.findByRole('button', { name: /Ver seguimiento de la entrega/ }))

    expect(await screen.findByText(/Adjudicado: 12 metros cúbicos/)).toHaveTextContent(
      'Recibido: 5 metros cúbicos (m³) · Por recibir: 7 metros cúbicos (m³)',
    )
    expect(screen.getByText('Motivo: Llegó incompleta')).toBeInTheDocument()
    expect(screen.getByText('Remito R-9')).toBeInTheDocument()
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
