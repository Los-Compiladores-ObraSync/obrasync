import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it } from 'vitest'
import { reiniciarLicitacionesMock } from '../mocks/handlers'
import { server } from '../mocks/server'
import { ProveedoresPage } from './ProveedoresPage'

function renderPagina() {
  return render(
    <MemoryRouter>
      <ProveedoresPage />
    </MemoryRouter>,
  )
}

describe('ProveedoresPage', () => {
  beforeEach(() => {
    reiniciarLicitacionesMock()
  })

  it('muestra el estado de carga y luego lista las licitaciones abiertas', async () => {
    renderPagina()

    expect(screen.getByRole('status')).toHaveTextContent('Cargando…')

    expect(await screen.findByText('REQ-2026-0011')).toBeInTheDocument()
    expect(screen.getByText('REQ-2026-0012')).toBeInTheDocument()
    expect(screen.getByText('2 ítems')).toBeInTheDocument()
    expect(screen.getByText('3 ítems')).toBeInTheDocument()
    expect(screen.getByText(/20 de noviembre de 2026/)).toBeInTheDocument()
    expect(screen.getByText(/25 de noviembre de 2026/)).toBeInTheDocument()

    const enlacesCotizar = screen.getAllByRole('link', { name: 'Cotizar' })
    expect(enlacesCotizar).toHaveLength(2)
    expect(enlacesCotizar[0]).toHaveAttribute(
      'href',
      '/proveedores/licitaciones/b1b2c3d4-1111-0000-0000-000000000001',
    )
  })

  it('tiene un enlace para volver al inicio', async () => {
    renderPagina()

    expect(screen.getByRole('link', { name: '← Volver al inicio' })).toHaveAttribute('href', '/')
    expect(await screen.findByText('REQ-2026-0011')).toBeInTheDocument()
  })

  it('muestra mensaje cuando no hay licitaciones abiertas', async () => {
    server.use(
      http.get('*/api/licitaciones', () => {
        return HttpResponse.json([], { status: 200 })
      }),
    )

    renderPagina()

    expect(
      await screen.findByText('No hay licitaciones abiertas por ahora'),
    ).toBeInTheDocument()
  })

  it('muestra error y permite reintentar cuando falla el servidor', async () => {
    const user = userEvent.setup()
    let fallo = true

    server.use(
      http.get('*/api/licitaciones', () => {
        if (fallo) {
          return HttpResponse.json(
            { title: 'Error', status: 500, detail: 'Error de servidor' },
            { status: 500 },
          )
        }
        return HttpResponse.json([
          {
            licitacionId: 'lic-1',
            requisicionId: 'req-1',
            numeroRequisicion: 'REQ-2026-9999',
            obraId: 'obra-1',
            estado: 'Abierta',
            fechaCreacion: '2026-10-20T10:00:00Z',
            fechaLimiteCotizacion: '2026-11-20T18:00:00Z',
            items: [{ articuloId: 'art-1', descripcion: 'Cemento', cantidad: 10, unidadMedida: 'BOLSA' }],
          },
        ])
      }),
    )

    renderPagina()

    expect(
      await screen.findByText('No se pudieron cargar las licitaciones. Intentá nuevamente.'),
    ).toBeInTheDocument()

    fallo = false
    await user.click(screen.getByRole('button', { name: 'Reintentar' }))

    expect(await screen.findByText('REQ-2026-9999')).toBeInTheDocument()
  })
})
