import { render, screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AuthContext, type SesionUsuario } from '../auth/sessionContext'
import { reiniciarOrdenesCompraMock } from '../mocks/handlers'
import { server } from '../mocks/server'
import { OrdenesCompraPage } from './OrdenesCompraPage'

vi.mock('../chat/ChatOrden', () => ({ ChatOrden: () => null }))
vi.mock('../chat/useChatEnLista', () => ({
  useChatEnLista: () => ({
    problemaDe: (orden: { tieneProblemaAbierto?: boolean; motivoProblemaAbierto?: string }) => ({
      abierto: orden.tieneProblemaAbierto === true, motivo: orden.motivoProblemaAbierto ?? null,
    }),
    noLeidosDe: () => 0,
    marcarLeida: () => {},
    cambiarProblema: () => {},
  }),
}))

const comprador: SesionUsuario = { id: 'comp-1', nombreCompleto: 'Comprador', email: 'c@x.com', rol: 'Comprador' }

const orden = {
  id: 'eeeeeeee-0000-0000-0000-00000000000b',
  licitacionId: 'l1',
  cotizacionId: 'c1',
  obraId: 'o1',
  proveedorId: 'p1',
  estado: 'Despachada',
  fechaEmision: '2026-11-10T12:00:00Z',
  fueDespachada: true,
  fechaDespacho: '2026-11-12T12:00:00Z',
  recibidaCompleta: false,
  puedeRedespachar: true,
  tieneRecepcionParcialPendiente: true,
}

function renderPagina() {
  return render(
    <AuthContext.Provider value={{ usuario: comprador, login: () => {}, logout: () => {} }}>
      <MemoryRouter initialEntries={['/compras/ordenes']}>
        <OrdenesCompraPage />
      </MemoryRouter>
    </AuthContext.Provider>,
  )
}

describe('OrdenesCompraPage: entrega parcial (#249)', () => {
  beforeEach(() => reiniciarOrdenesCompraMock())

  it('muestra el estado intermedio y que falta el re-despacho del proveedor', async () => {
    server.use(http.get('*/api/ordenes-compra', () => HttpResponse.json([orden])))
    renderPagina()

    expect(await screen.findByText(/Falta que el proveedor despache el resto/)).toBeInTheDocument()
    expect(screen.getByText('Recepción parcial', { selector: 'span' })).toBeInTheDocument()
    expect(screen.queryByText(/La recepción está completa/)).not.toBeInTheDocument()
    expect(screen.queryByText(/Siguiente paso: Recepción debe registrar/)).not.toBeInTheDocument()
  })

  it('prioriza el motivo del problema cuando la orden espera un nuevo despacho', async () => {
    server.use(http.get('*/api/ordenes-compra', () => HttpResponse.json([
      { ...orden, tieneRecepcionParcialPendiente: false, tieneProblemaAbierto: true, motivoProblemaAbierto: 'Llegó dañada' },
    ])))
    renderPagina()

    expect(await screen.findByText('Pendiente de nuevo despacho')).toBeInTheDocument()
    expect(screen.getByText('Entrega con problema: Llegó dañada')).toBeInTheDocument()
    expect(screen.queryByText('Recepción parcial')).not.toBeInTheDocument()
  })

  it('una orden despachada sin parcial sigue mostrando "Despachada"', async () => {
    server.use(
      http.get('*/api/ordenes-compra', () =>
        HttpResponse.json([{ ...orden, puedeRedespachar: false, tieneRecepcionParcialPendiente: false }]),
      ),
    )
    renderPagina()

    expect(await screen.findByText('Despachada', { selector: 'span' })).toBeInTheDocument()
    expect(screen.queryByText('Recepción parcial', { selector: 'span' })).not.toBeInTheDocument()
  })
})
