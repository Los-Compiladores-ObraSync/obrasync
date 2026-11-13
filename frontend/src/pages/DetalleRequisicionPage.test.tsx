import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter, Route, Routes } from 'react-router'
import { beforeEach, describe, expect, it } from 'vitest'
import { AuthContext, type SesionUsuario } from '../auth/sessionContext'
import { reiniciarLicitacionesMock, reiniciarRequisicionesMock } from '../mocks/handlers'
import { server } from '../mocks/server'
import { DetalleRequisicionPage } from './DetalleRequisicionPage'

const ID_APROBADA = 'd1d2d3d4-0000-0000-0000-000000000009'
const ID_RECHAZADA = 'd1d2d3d4-0000-0000-0000-000000000010'
const ID_PENDIENTE = 'd1d2d3d4-0000-0000-0000-000000000011'

const comprador: SesionUsuario = {
  id: 'a1b2c3d4-0000-0000-0000-000000000002',
  nombreCompleto: 'Marta Gómez',
  email: 'comprador@obrasync.com',
  rol: 'Comprador',
}
const capataz: SesionUsuario = {
  id: 'a1b2c3d4-0000-0000-0000-000000000001',
  nombreCompleto: 'Juan Pérez',
  email: 'capataz@obrasync.com',
  rol: 'Capataz',
}

function renderPagina(usuario: SesionUsuario, id: string, search = '') {
  return render(
    <AuthContext.Provider value={{ usuario, login: () => {}, logout: () => {} }}>
      <MemoryRouter initialEntries={[`/compras/requisiciones/historial/${id}${search}`]}>
        <Routes>
          <Route
            path="/compras/requisiciones/historial/:id"
            element={<DetalleRequisicionPage volverA="/compras/requisiciones/historial" />}
          />
        </Routes>
      </MemoryRouter>
    </AuthContext.Provider>,
  )
}

describe('DetalleRequisicionPage', () => {
  beforeEach(() => {
    reiniciarRequisicionesMock()
    reiniciarLicitacionesMock()
  })

  it('muestra una requisición aprobada con obra, etapa, materiales y fechas', async () => {
    renderPagina(comprador, ID_APROBADA)

    expect(
      await screen.findByRole('heading', { level: 1, name: 'REQ-2026-0009' }),
    ).toBeInTheDocument()
    expect(screen.getByText('Aprobada')).toBeInTheDocument()
    expect(screen.getByText('Obra: Torre Alvear')).toBeInTheDocument()
    expect(screen.getByText('Etapa: Fundaciones')).toBeInTheDocument()
    expect(screen.getByText('Creada el 15 de octubre de 2026')).toBeInTheDocument()
    expect(screen.getByText('Aprobada el 16 de octubre de 2026')).toBeInTheDocument()
    expect(screen.getByText(/12 metros cúbicos \(m³\) · Arena Fina a Granel/)).toBeInTheDocument()
  })

  it('una rechazada muestra las observaciones y no tiene fecha de aprobación', async () => {
    renderPagina(comprador, ID_RECHAZADA)

    expect(await screen.findByText('Rechazada')).toBeInTheDocument()
    expect(screen.getByText('Observaciones: Cantidad fuera de presupuesto')).toBeInTheDocument()
    expect(screen.queryByText(/Aprobada el/)).not.toBeInTheDocument()
  })

  it('mientras carga muestra "Cargando…"', () => {
    renderPagina(comprador, ID_APROBADA)

    expect(screen.getByRole('status')).toHaveTextContent('Cargando…')
  })

  it('el enlace de volver conserva los filtros del historial', async () => {
    renderPagina(comprador, ID_APROBADA, '?estado=Aprobada')

    const volver = await screen.findByRole('link', { name: '← Volver al historial' })
    expect(volver).toHaveAttribute('href', '/compras/requisiciones/historial?estado=Aprobada')
  })

  it('si la requisición no existe lo dice claro', async () => {
    renderPagina(comprador, 'no-existe')

    expect(await screen.findByRole('alert')).toHaveTextContent('No encontramos esa requisición.')
    expect(screen.queryByRole('button', { name: 'Reintentar' })).not.toBeInTheDocument()
  })

  it('si falla el servidor ofrece reintentar y, al reintentar, la muestra', async () => {
    server.use(
      http.get(
        '*/api/requisiciones/:id',
        () => HttpResponse.json({ title: 'Error', status: 500 }, { status: 500 }),
        { once: true },
      ),
    )
    const user = userEvent.setup()
    renderPagina(comprador, ID_PENDIENTE)

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'No se pudo cargar la requisición. Intentá nuevamente.',
    )
    await user.click(screen.getByRole('button', { name: 'Reintentar' }))

    expect(await screen.findByRole('heading', { level: 1, name: 'REQ-2026-0011' })).toBeInTheDocument()
  })

  it('el Capataz ve la suya', async () => {
    renderPagina(capataz, ID_PENDIENTE)

    expect(await screen.findByRole('heading', { level: 1, name: 'REQ-2026-0011' })).toBeInTheDocument()
  })

  it('el Capataz no ve la de otra persona: se muestra como si no existiera', async () => {
    renderPagina(capataz, ID_RECHAZADA)

    expect(await screen.findByRole('alert')).toHaveTextContent('No encontramos esa requisición.')
    expect(screen.queryByText('REQ-2026-0010')).not.toBeInTheDocument()
  })

  it('si falla la lista de obras muestra el identificador de la obra', async () => {
    server.use(
      http.get('*/api/obras', () => HttpResponse.json({ title: 'Error', status: 500 }, { status: 500 })),
    )
    renderPagina(comprador, ID_APROBADA)

    expect(await screen.findByText(/^Obra: b1b2c3d4-/)).toBeInTheDocument()
  })
  describe('publicar licitación', () => {
    it('el Comprador ve el botón en una requisición aprobada y la publica', async () => {
      const user = userEvent.setup()
      renderPagina(comprador, ID_APROBADA)

      await user.click(await screen.findByRole('button', { name: 'Publicar licitación' }))

      expect(await screen.findByRole('status')).toHaveTextContent('Licitación publicada.')
      expect(screen.queryByRole('button', { name: 'Publicar licitación' })).not.toBeInTheDocument()
    })

    it('manda la requisición y una fecha límite futura en UTC', async () => {
      let cuerpo: { requisicionId?: string; fechaLimiteCotizacion?: string } = {}
      server.use(
        http.post('*/api/licitaciones', async ({ request }) => {
          cuerpo = (await request.json()) as typeof cuerpo
          return HttpResponse.json(
            { licitacionId: 'x', fechaLimiteCotizacion: cuerpo.fechaLimiteCotizacion },
            { status: 201 },
          )
        }),
      )
      const user = userEvent.setup()
      renderPagina(comprador, ID_APROBADA)

      await user.click(await screen.findByRole('button', { name: 'Publicar licitación' }))
      await screen.findByRole('status')

      expect(cuerpo.requisicionId).toBe(ID_APROBADA)
      expect(new Date(cuerpo.fechaLimiteCotizacion ?? '').getTime()).toBeGreaterThan(Date.now())
    })

    it('no ofrece publicar si la requisición no está aprobada', async () => {
      renderPagina(comprador, ID_PENDIENTE)

      await screen.findByRole('heading', { level: 1, name: 'REQ-2026-0011' })
      expect(screen.queryByRole('button', { name: 'Publicar licitación' })).not.toBeInTheDocument()
    })

    it('el Capataz no ve el botón', async () => {
      renderPagina(capataz, ID_PENDIENTE)

      await screen.findByRole('heading', { level: 1, name: 'REQ-2026-0011' })
      expect(screen.queryByRole('button', { name: 'Publicar licitación' })).not.toBeInTheDocument()
    })

    it('si ya hay una licitación abierta lo dice claro y deja reintentar', async () => {
      server.use(
        http.post('*/api/licitaciones', () =>
          HttpResponse.json({ title: 'Conflicto', status: 409 }, { status: 409 }),
        ),
      )
      const user = userEvent.setup()
      renderPagina(comprador, ID_APROBADA)

      await user.click(await screen.findByRole('button', { name: 'Publicar licitación' }))

      expect(await screen.findByRole('alert')).toHaveTextContent(
        'Ya hay una licitación abierta para esta requisición.',
      )
      expect(screen.getByRole('button', { name: 'Publicar licitación' })).toBeEnabled()
    })

    it('si falla el servidor muestra un mensaje general', async () => {
      server.use(
        http.post('*/api/licitaciones', () =>
          HttpResponse.json({ title: 'Error', status: 500 }, { status: 500 }),
        ),
      )
      const user = userEvent.setup()
      renderPagina(comprador, ID_APROBADA)

      await user.click(await screen.findByRole('button', { name: 'Publicar licitación' }))

      expect(await screen.findByRole('alert')).toHaveTextContent(
        'No se pudo publicar la licitación. Intentá nuevamente.',
      )
    })
  })
})
