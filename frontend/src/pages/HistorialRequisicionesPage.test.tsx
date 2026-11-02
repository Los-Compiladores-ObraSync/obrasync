import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it } from 'vitest'
import { AuthContext, type SesionUsuario } from '../auth/sessionContext'
import { reiniciarRequisicionesMock } from '../mocks/handlers'
import { server } from '../mocks/server'
import { HistorialRequisicionesPage } from './HistorialRequisicionesPage'

const ID_CAPATAZ = 'a1b2c3d4-0000-0000-0000-000000000001'

function renderPagina(usuario: SesionUsuario) {
  return render(
    <AuthContext.Provider value={{ usuario, login: () => {}, logout: () => {} }}>
      <MemoryRouter>
        <HistorialRequisicionesPage volverA="/compras" textoVolver="← Volver a Gestión y Compras" />
      </MemoryRouter>
    </AuthContext.Provider>,
  )
}

const comprador: SesionUsuario = {
  id: 'a1b2c3d4-0000-0000-0000-000000000002',
  nombreCompleto: 'Marta Gómez',
  email: 'comprador@obrasync.com',
  rol: 'Comprador',
}
const capataz: SesionUsuario = {
  id: ID_CAPATAZ,
  nombreCompleto: 'Juan Pérez',
  email: 'capataz@obrasync.com',
  rol: 'Capataz',
}

describe('HistorialRequisicionesPage', () => {
  beforeEach(() => {
    reiniciarRequisicionesMock()
  })

  it('el Comprador ve las requisiciones de todos los estados con obra, etapa y estado', async () => {
    renderPagina(comprador)

    expect(await screen.findByText('REQ-2026-0011')).toBeInTheDocument()
    expect(screen.getByText('REQ-2026-0012')).toBeInTheDocument()
    expect(screen.getByText('REQ-2026-0009')).toBeInTheDocument()
    expect(screen.getByText('REQ-2026-0010')).toBeInTheDocument()

    const tarjeta = screen.getByText('REQ-2026-0009').closest('li')!
    expect(within(tarjeta).getByText('Aprobada')).toBeInTheDocument()
    expect(within(tarjeta).getByText(/Obra: Torre Alvear · Etapa: Fundaciones/)).toBeInTheDocument()
    expect(within(tarjeta).getByText(/Creada el 15 de octubre de 2026/)).toBeInTheDocument()
    expect(
      within(screen.getByText('REQ-2026-0010').closest('li')!).getByText('Rechazada'),
    ).toBeInTheDocument()
  })

  it('el Capataz ve solo las requisiciones que pidió él', async () => {
    renderPagina(capataz)

    expect(await screen.findByText('REQ-2026-0011')).toBeInTheDocument()
    expect(screen.getByText('REQ-2026-0009')).toBeInTheDocument()
    // La 0010 la pidió otra persona.
    expect(screen.queryByText('REQ-2026-0010')).not.toBeInTheDocument()
  })

  it('el Capataz sin requisiciones ve un mensaje a su medida', async () => {
    server.use(http.get('*/api/requisiciones', () => HttpResponse.json([])))
    renderPagina(capataz)

    expect(await screen.findByText('Todavía no hiciste ninguna requisición.')).toBeInTheDocument()
  })

  it('el Comprador sin requisiciones ve el aviso de lista vacía', async () => {
    server.use(http.get('*/api/requisiciones', () => HttpResponse.json([])))
    renderPagina(comprador)

    expect(await screen.findByText('Todavía no hay requisiciones.')).toBeInTheDocument()
  })

  it('muestra el identificador si no se pueden traer las obras', async () => {
    server.use(http.get('*/api/obras', () => HttpResponse.error()))
    renderPagina(comprador)

    expect(await screen.findByText('REQ-2026-0011')).toBeInTheDocument()
    expect(screen.getAllByText(/Obra: b1b2c3d4-0000-0000-0000-0000000000a1/).length).toBeGreaterThan(0)
  })

  it('muestra el error y Reintentar si falla la carga, y se recupera al reintentar', async () => {
    let falla = true
    server.use(
      http.get('*/api/requisiciones', () =>
        falla ? HttpResponse.error() : HttpResponse.json([]),
      ),
    )
    const user = userEvent.setup()
    renderPagina(comprador)

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'No se pudieron cargar las requisiciones.',
    )

    falla = false
    await user.click(screen.getByRole('button', { name: 'Reintentar' }))

    expect(await screen.findByText('Todavía no hay requisiciones.')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('el enlace de arriba vuelve al portal indicado', async () => {
    renderPagina(comprador)

    await screen.findByText('REQ-2026-0011')
    expect(screen.getByRole('link', { name: '← Volver a Gestión y Compras' })).toHaveAttribute(
      'href',
      '/compras',
    )
  })
})
