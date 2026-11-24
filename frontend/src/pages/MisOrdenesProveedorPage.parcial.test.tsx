import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it } from 'vitest'
import { reiniciarOrdenesCompraMock } from '../mocks/handlers'
import { server } from '../mocks/server'
import { MisOrdenesProveedorPage } from './MisOrdenesProveedorPage'

// Entrega parcial y re-despacho del remanente (#249). Los campos vienen del contrato de Licitaciones (#248).
const RUTA_LISTA = '*/api/ordenes-compra'
const RUTA_DESPACHAR = '*/api/ordenes-compra/:id/despachar'

const parcial = {
  id: 'dddddddd-0000-0000-0000-00000000000a',
  licitacionId: 'l1',
  cotizacionId: 'c1',
  obraId: 'o1',
  proveedorId: 'p1',
  estado: 'Despachada',
  fechaEmision: '2026-11-10T12:00:00Z',
  fueDespachada: true,
  fechaDespacho: '2026-11-12T12:00:00Z',
  numeroDespacho: 1,
  puedeRedespachar: true,
  tieneRecepcionParcialPendiente: true,
  fechaUltimaRecepcionParcial: '2026-11-15T12:00:00Z',
}

describe('MisOrdenesProveedorPage: entrega parcial (#249)', () => {
  beforeEach(() => reiniciarOrdenesCompraMock())

  it('una orden con recepción parcial se muestra como tal y ofrece despachar lo que falta', async () => {
    server.use(http.get(RUTA_LISTA, () => HttpResponse.json([parcial])))
    renderPagina()

    expect(await screen.findByRole('button', { name: /^Despachar lo que falta/ })).toBeInTheDocument()
    expect(screen.getByText(/mercadería pendiente de despacho/)).toBeInTheDocument()
    expect(screen.getByText('Recepción parcial', { selector: 'span' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^Marcar como despachada/ })).not.toBeInTheDocument()
  })

  it('re-despacha el remanente y avisa que Compras y el Capataz ya lo ven', async () => {
    let llamadas = 0
    server.use(
      http.get(RUTA_LISTA, () => HttpResponse.json([parcial])),
      http.post(RUTA_DESPACHAR, ({ params }) => {
        llamadas++
        return HttpResponse.json({
          ...parcial,
          id: params.id,
          numeroDespacho: 2,
          puedeRedespachar: false,
          tieneRecepcionParcialPendiente: false,
        })
      }),
    )
    const user = userEvent.setup()
    renderPagina()

    await user.click(await screen.findByRole('button', { name: /^Despachar lo que falta/ }))
    await user.click(screen.getByRole('button', { name: 'Confirmar despacho' }))

    expect(await screen.findByRole('status')).toHaveTextContent('despachaste lo que faltaba de la orden DDDDDDDD')
    expect(llamadas).toBe(1)
    expect(screen.queryByRole('button', { name: /^Despachar lo que falta/ })).not.toBeInTheDocument()
  })

  it('una orden despachada sin remanente no ofrece despachar de nuevo', async () => {
    server.use(
      http.get(RUTA_LISTA, () =>
        HttpResponse.json([{ ...parcial, puedeRedespachar: false, tieneRecepcionParcialPendiente: false }]),
      ),
    )
    renderPagina()

    const fila = (await screen.findAllByRole('listitem'))[0]
    expect(within(fila).queryByRole('button', { name: /Despachar lo que falta|Marcar como despachada/ })).not.toBeInTheDocument()
    expect(within(fila).getByText('Despachada')).toBeInTheDocument()
  })
})

function renderPagina() {
  return render(
    <MemoryRouter>
      <MisOrdenesProveedorPage />
    </MemoryRouter>,
  )
}
