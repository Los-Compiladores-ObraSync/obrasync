import { render, screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import { server } from '../mocks/server'
import { MiReputacionProveedorPage } from './MiReputacionProveedorPage'

function renderPagina() {
  return render(
    <MemoryRouter>
      <MiReputacionProveedorPage />
    </MemoryRouter>,
  )
}

describe('MiReputacionProveedorPage (#222)', () => {
  it('muestra promedio, cantidad y los comentarios que devuelve el endpoint autenticado', async () => {
    let url = ''
    server.use(http.get('*/api/cotizaciones/proveedores/mi-reputacion', ({ request }) => {
      url = request.url
      return HttpResponse.json({
        proveedorId: 'proveedor-autenticado',
        calificacion: 4.6,
        cantidadCalificaciones: 12,
        fechaActualizacion: '2026-11-20T12:00:00Z',
        ultimosComentarios: [
          {
            ordenCompraId: 'orden-1',
            calificacion: 5,
            comentario: 'Entrega puntual y completa.',
            fechaActualizacion: '2026-11-20T12:00:00Z',
          },
          {
            ordenCompraId: 'orden-2',
            calificacion: 4,
            comentario: 'Buena comunicación.',
            fechaActualizacion: '2026-11-18T12:00:00Z',
          },
        ],
      })
    }))
    renderPagina()

    expect(await screen.findByText(/4,6 ★ con 12 calificaciones/)).toBeInTheDocument()
    expect(screen.getByText('Entrega puntual y completa.')).toBeInTheDocument()
    expect(screen.getByText('Buena comunicación.')).toBeInTheDocument()
    expect(url).toContain('/api/cotizaciones/proveedores/mi-reputacion')
    expect(new URL(url).searchParams.has('proveedorId')).toBe(false)
    expect(screen.queryByText('proveedor-autenticado')).not.toBeInTheDocument()
  })

  it('muestra el estado vacío que define el contrato cuando aún no hay calificaciones', async () => {
    server.use(http.get('*/api/cotizaciones/proveedores/mi-reputacion', () =>
      HttpResponse.json({ proveedorId: 'propio', calificacion: null, cantidadCalificaciones: 0, fechaActualizacion: null, ultimosComentarios: [] }),
    ))
    renderPagina()

    expect(await screen.findByText('Todavía no recibiste calificaciones.')).toBeInTheDocument()
    expect(screen.getByText('Todavía no hay comentarios.')).toBeInTheDocument()
  })
})
