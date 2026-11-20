import { render, screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { MemoryRouter, Route, Routes } from 'react-router'
import { beforeEach, describe, expect, it } from 'vitest'
import { AuthContext, type SesionUsuario } from '../auth/sessionContext'
import { reiniciarRequisicionesMock } from '../mocks/handlers'
import { server } from '../mocks/server'
import { SeguimientoPedidoPage } from './SeguimientoPedidoPage'

const ID_APROBADA = 'd1d2d3d4-0000-0000-0000-000000000009'
const ID_PENDIENTE = 'd1d2d3d4-0000-0000-0000-000000000011'
const ID_RECHAZADA = 'd1d2d3d4-0000-0000-0000-000000000013'

const capataz: SesionUsuario = {
  id: 'a1b2c3d4-0000-0000-0000-000000000001',
  nombreCompleto: 'Juan Pérez',
  email: 'capataz@obrasync.com',
  rol: 'Capataz',
}

function renderPagina(id: string) {
  return render(
    <AuthContext.Provider value={{ usuario: capataz, login: () => {}, logout: () => {} }}>
      <MemoryRouter initialEntries={[`/obra/requisiciones/${id}/seguimiento`]}>
        <Routes>
          <Route path="/obra/requisiciones/:id/seguimiento" element={<SeguimientoPedidoPage />} />
        </Routes>
      </MemoryRouter>
    </AuthContext.Provider>,
  )
}

describe('SeguimientoPedidoPage', () => {
  beforeEach(() => reiniciarRequisicionesMock())

  it('un pedido pendiente queda esperando a Compras', async () => {
    renderPagina(ID_PENDIENTE)

    expect(await screen.findByRole('heading', { level: 1, name: 'Seguimiento de REQ-2026-0011' })).toBeInTheDocument()
    expect(screen.getByText(/Esperando que Compras lo apruebe/)).toBeInTheDocument()
  })

  it('un pedido aprobado muestra la licitación abierta con sus ofertas', async () => {
    renderPagina(ID_APROBADA)

    expect(await screen.findByText(/Los proveedores están cotizando/)).toBeInTheDocument()
    expect(screen.getByText(/2 ofertas recibidas/)).toBeInTheDocument()
    expect(screen.getByText(/Compras lo aprobó/)).toBeInTheDocument()
  })

  it('un pedido rechazado muestra el motivo', async () => {
    renderPagina(ID_RECHAZADA)

    expect(await screen.findByText(/Compras lo rechazó/)).toBeInTheDocument()
  })

  it('si el pedido no existe avisa sin mostrar nada más', async () => {
    server.use(
      http.get('*/api/requisiciones/:id', () => HttpResponse.json({ title: 'No encontrada', status: 404 }, { status: 404 })),
    )
    renderPagina(ID_APROBADA)

    expect(await screen.findByRole('alert')).toHaveTextContent('No encontramos ese pedido.')
  })
})
