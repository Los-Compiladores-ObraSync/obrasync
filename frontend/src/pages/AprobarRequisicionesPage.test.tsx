import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it } from 'vitest'
import { reiniciarRequisicionesMock } from '../mocks/handlers'
import { server } from '../mocks/server'
import { AprobarRequisicionesPage } from './AprobarRequisicionesPage'

function renderPagina() {
  return render(
    <MemoryRouter>
      <AprobarRequisicionesPage />
    </MemoryRouter>,
  )
}

describe('AprobarRequisicionesPage', () => {
  beforeEach(() => {
    reiniciarRequisicionesMock()
  })

  it('lista las requisiciones pendientes con su obra', async () => {
    renderPagina()

    expect(await screen.findByText('REQ-2026-0011')).toBeInTheDocument()
    expect(screen.getByText('REQ-2026-0012')).toBeInTheDocument()
    expect(screen.getAllByText(/Obra: Torre Alvear/)).toHaveLength(2)
  })

  it('aprueba una requisición y la saca de la lista', async () => {
    const user = userEvent.setup()
    renderPagina()

    await screen.findByText('REQ-2026-0011')
    await user.click(screen.getByRole('button', { name: 'Aprobar REQ-2026-0011' }))

    expect(await screen.findByRole('status')).toHaveTextContent(
      'La requisición REQ-2026-0011 fue aprobada.',
    )
    expect(screen.queryByText('REQ-2026-0011')).not.toBeInTheDocument()
    expect(screen.getByText('REQ-2026-0012')).toBeInTheDocument()
  })

  it('muestra el detalle del servidor ante un 409 y conserva la lista', async () => {
    server.use(
      http.post('*/api/requisiciones/:id/aprobar', () =>
        HttpResponse.json({ status: 409, detail: 'Ya fue aprobada.' }, { status: 409 }),
      ),
    )
    const user = userEvent.setup()
    renderPagina()

    await screen.findByText('REQ-2026-0011')
    await user.click(screen.getByRole('button', { name: 'Aprobar REQ-2026-0011' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Ya fue aprobada.')
    expect(screen.getByText('REQ-2026-0011')).toBeInTheDocument()
  })

  it('avisa que no hay permiso ante un 403', async () => {
    server.use(
      http.post('*/api/requisiciones/:id/aprobar', () =>
        HttpResponse.json({ status: 403 }, { status: 403 }),
      ),
    )
    const user = userEvent.setup()
    renderPagina()

    await screen.findByText('REQ-2026-0011')
    await user.click(screen.getByRole('button', { name: 'Aprobar REQ-2026-0011' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'No tenés permiso para aprobar requisiciones.',
    )
  })

  it('muestra un aviso si no hay pendientes', async () => {
    server.use(http.get('*/api/requisiciones', () => HttpResponse.json([])))
    renderPagina()

    expect(
      await screen.findByText('No hay requisiciones pendientes de aprobación.'),
    ).toBeInTheDocument()
  })

  it('muestra error y Reintentar si falla la carga', async () => {
    server.use(http.get('*/api/requisiciones', () => HttpResponse.error()))
    renderPagina()

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'No se pudieron cargar las requisiciones.',
    )
    expect(screen.getByRole('button', { name: 'Reintentar' })).toBeInTheDocument()
  })
})
