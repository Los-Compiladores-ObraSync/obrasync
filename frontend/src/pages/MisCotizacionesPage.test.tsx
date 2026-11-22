import { fireEvent, render, screen } from '@testing-library/react'
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

function renderPagina(ruta = '/') {
  return render(
    <AuthContext.Provider value={{ usuario: proveedor, login: () => {}, logout: () => {} }}>
      <MemoryRouter initialEntries={[ruta]}>
        <MisCotizacionesPage />
      </MemoryRouter>
    </AuthContext.Provider>,
  )
}

/** Cuatro cotizaciones, una de cada situación posible. */
function cuatroCotizaciones() {
  return [
    cotizacion({ fechaCreacion: '2026-11-10T12:00:00Z' }), // REQ-2026-0011, en curso (abierta)
    cotizacion({
      cotizacionId: 'c0000000-0000-0000-0000-000000000002',
      numeroRequisicion: 'REQ-2026-0010',
      estadoLicitacion: 'Cerrada',
      estadoResultado: 'LicitacionCerrada',
      fechaCreacion: '2026-11-09T12:00:00Z',
    }),
    cotizacion({
      cotizacionId: 'c0000000-0000-0000-0000-000000000003',
      numeroRequisicion: 'REQ-2026-0009',
      estadoLicitacion: 'Adjudicada',
      estadoResultado: 'Gano',
      esGanadora: true,
      fechaCreacion: '2026-11-08T12:00:00Z',
    }),
    cotizacion({
      cotizacionId: 'c0000000-0000-0000-0000-000000000004',
      numeroRequisicion: 'REQ-2026-0008',
      estadoLicitacion: 'Adjudicada',
      estadoResultado: 'NoGano',
      fechaCreacion: '2026-11-07T12:00:00Z',
    }),
  ]
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
    expect(screen.getByText('Licitación adjudicada')).toBeInTheDocument()
    expect(screen.getByText('Licitación no adjudicada')).toBeInTheDocument()

    const corregir = screen.getAllByRole('link', { name: 'Editar cotización' })
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

  it('filtra por En curso, Adjudicadas y No adjudicadas, con su contador', async () => {
    server.use(http.get('*/api/cotizaciones/mias', () => HttpResponse.json(cuatroCotizaciones())))
    renderPagina()

    expect(await screen.findByText('REQ-2026-0011')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Quitar filtros' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'En curso (2)' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Adjudicadas (1)' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'No adjudicadas (1)' })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Adjudicadas (1)' }))
    expect(screen.getByText('REQ-2026-0009')).toBeInTheDocument()
    expect(screen.queryByText('REQ-2026-0011')).not.toBeInTheDocument()
    expect(screen.queryByText('REQ-2026-0008')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'No adjudicadas (1)' }))
    expect(screen.getByText('REQ-2026-0008')).toBeInTheDocument()
    expect(screen.queryByText('REQ-2026-0009')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'En curso (2)' }))
    expect(screen.getByText('REQ-2026-0011')).toBeInTheDocument()
    expect(screen.getByText('REQ-2026-0010')).toBeInTheDocument()
    expect(screen.queryByText('REQ-2026-0008')).not.toBeInTheDocument()

    // Tocar el botón activo lo desactiva, y "Quitar filtros" vuelve a mostrar todas.
    fireEvent.click(screen.getByRole('button', { name: 'En curso (2)' }))
    expect(screen.getByText('REQ-2026-0008')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Adjudicadas (1)' }))
    expect(screen.queryByText('REQ-2026-0008')).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Quitar filtros' }))
    expect(screen.getByText('REQ-2026-0008')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Quitar filtros' })).toBeDisabled()
  })

  it('una licitación cerrada sin adjudicar queda en En curso y no cuenta como perdida', async () => {
    server.use(
      http.get('*/api/cotizaciones/mias', () =>
        HttpResponse.json([
          cotizacion({ estadoLicitacion: 'Cerrada', estadoResultado: 'LicitacionCerrada' }),
        ]),
      ),
    )
    renderPagina()

    expect(await screen.findByText('REQ-2026-0011')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'No adjudicadas (0)' }))
    expect(screen.getByText('Ninguna de tus cotizaciones quedó sin adjudicar.')).toBeInTheDocument()
    expect(screen.queryByText('REQ-2026-0011')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'En curso (1)' }))
    expect(screen.getByText('REQ-2026-0011')).toBeInTheDocument()
  })

  it('ofrece ir a Mis órdenes solo en la cotización ganada', async () => {
    server.use(http.get('*/api/cotizaciones/mias', () => HttpResponse.json(cuatroCotizaciones())))
    renderPagina()

    await screen.findByText('REQ-2026-0011')
    const enlaces = screen.getAllByRole('link', { name: 'Ver mis órdenes' })
    expect(enlaces).toHaveLength(1)
    expect(enlaces[0]).toHaveAttribute('href', '/proveedores/mis-ordenes')
  })

  it('toma el filtro de la dirección de la página', async () => {
    server.use(http.get('*/api/cotizaciones/mias', () => HttpResponse.json(cuatroCotizaciones())))
    renderPagina('/?estado=adjudicadas')

    expect(await screen.findByText('REQ-2026-0009')).toBeInTheDocument()
    expect(screen.queryByText('REQ-2026-0011')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Adjudicadas (1)' })).toHaveAttribute('aria-pressed', 'true')
  })

  it('muestra primero la cotización más reciente', async () => {
    server.use(
      http.get('*/api/cotizaciones/mias', () => HttpResponse.json([...cuatroCotizaciones()].reverse())),
    )
    renderPagina()

    await screen.findByText('REQ-2026-0011')
    const titulos = screen.getAllByRole('heading', { level: 2 }).map((h) => h.textContent)
    expect(titulos).toEqual(['REQ-2026-0011', 'REQ-2026-0010', 'REQ-2026-0009', 'REQ-2026-0008'])
  })
})
