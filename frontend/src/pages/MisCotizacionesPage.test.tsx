import { render, screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import { AuthContext, type SesionUsuario } from '../auth/sessionContext'
import { server } from '../mocks/server'
import { MisCotizacionesPage } from './MisCotizacionesPage'

const proveedor: SesionUsuario = {
  id: 'a1b2c3d4-0000-0000-0000-000000000003',
  nombreCompleto: 'Carlos Ruiz',
  email: 'proveedor@obrasync.com',
  rol: 'Proveedor',
}

function cotizacion(sobre: Record<string, unknown>) {
  return {
    cotizacionId: 'c0000000-0000-0000-0000-000000000001',
    licitacionId: 'b0000000-0000-0000-0000-000000000001',
    proveedorId: proveedor.id,
    numeroRequisicion: 'REQ-2026-0011',
    obraId: 'o1',
    fechaLimiteCotizacion: '2099-01-01T12:00:00Z',
    estadoLicitacion: 'Abierta',
    subtotalItems: 90000,
    flete: 10000,
    costoTotal: 100000,
    coberturaParcial: false,
    plazoEntregaDias: 5,
    fechaCreacion: '2026-11-10T12:00:00Z',
    items: [],
    esGanadora: false,
    estadoResultado: 'Enviada',
    ...sobre,
  }
}

function renderPagina() {
  return render(
    <AuthContext.Provider value={{ usuario: proveedor, login: () => {}, logout: () => {} }}>
      <MemoryRouter>
        <MisCotizacionesPage />
      </MemoryRouter>
    </AuthContext.Provider>,
  )
}

describe('MisCotizacionesPage', () => {
  it('muestra el resultado de cada cotización y deja corregir solo la que sigue abierta', async () => {
    server.use(
      http.get('*/api/cotizaciones/mias', () =>
        HttpResponse.json([
          cotizacion({}),
          cotizacion({
            cotizacionId: 'c0000000-0000-0000-0000-000000000002',
            numeroRequisicion: 'REQ-2026-0009',
            estadoLicitacion: 'Adjudicada',
            estadoResultado: 'Gano',
            esGanadora: true,
          }),
          cotizacion({
            cotizacionId: 'c0000000-0000-0000-0000-000000000003',
            numeroRequisicion: 'REQ-2026-0008',
            estadoLicitacion: 'Adjudicada',
            estadoResultado: 'NoGano',
          }),
        ]),
      ),
    )

    renderPagina()

    expect(await screen.findByText('REQ-2026-0011')).toBeInTheDocument()
    expect(screen.getByText('Enviada, esperando el cierre')).toBeInTheDocument()
    expect(screen.getByText('¡Ganaste esta licitación!')).toBeInTheDocument()
    expect(screen.getByText('No ganaste esta licitación')).toBeInTheDocument()

    const corregir = screen.getAllByRole('link', { name: 'Corregir mi cotización' })
    expect(corregir).toHaveLength(1)
    expect(corregir[0]).toHaveAttribute(
      'href',
      '/proveedores/licitaciones/b0000000-0000-0000-0000-000000000001?corregir=c0000000-0000-0000-0000-000000000001',
    )
  })

  it('muestra su calificación cuando existe', async () => {
    server.use(
      http.get('*/api/cotizaciones/mias', () => HttpResponse.json([])),
      http.get('*/api/proveedores/:id/calificacion', () =>
        HttpResponse.json({
          proveedorId: proveedor.id,
          calificacion: 4.5,
          cantidadCalificaciones: 2,
          fechaActualizacion: '2026-11-10T12:00:00Z',
        }),
      ),
    )

    renderPagina()

    expect(await screen.findByText(/Tu calificación: 4\.5 de 5 \(2 evaluaciones\)/)).toBeInTheDocument()
    expect(await screen.findByText('Todavía no enviaste cotizaciones.')).toBeInTheDocument()
  })

  it('avisa que todavía no tiene calificación cuando el servicio responde 404', async () => {
    server.use(
      http.get('*/api/cotizaciones/mias', () => HttpResponse.json([])),
      http.get('*/api/proveedores/:id/calificacion', () =>
        HttpResponse.json({ title: 'No encontrada', status: 404 }, { status: 404 }),
      ),
    )

    renderPagina()

    expect(await screen.findByText(/Todavía no tenés calificación/)).toBeInTheDocument()
  })

  it('muestra un error con reintento si falla la carga', async () => {
    server.use(http.get('*/api/cotizaciones/mias', () => HttpResponse.json({}, { status: 500 })))

    renderPagina()

    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudieron cargar tus cotizaciones')
    expect(screen.getByRole('button', { name: 'Reintentar' })).toBeInTheDocument()
  })
})
