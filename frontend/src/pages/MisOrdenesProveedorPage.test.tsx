import { render, screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import { server } from '../mocks/server'
import { MisOrdenesProveedorPage } from './MisOrdenesProveedorPage'

function renderPagina() {
  return render(
    <MemoryRouter>
      <MisOrdenesProveedorPage />
    </MemoryRouter>,
  )
}

const base = {
  licitacionId: 'l1',
  cotizacionId: 'c1',
  obraId: 'o1',
  proveedorId: 'p1',
}

describe('MisOrdenesProveedorPage', () => {
  it('lista las órdenes y distingue las despachadas de las pendientes', async () => {
    server.use(
      http.get('*/api/ordenes-compra', () =>
        HttpResponse.json([
          { ...base, id: 'aaaaaaaa-0000', estado: 'Emitida', fechaEmision: '2026-11-18T12:00:00Z', fueDespachada: false },
          {
            ...base,
            id: 'bbbbbbbb-0000',
            estado: 'Despachada',
            fechaEmision: '2026-11-12T12:00:00Z',
            fueDespachada: true,
            fechaDespacho: '2026-11-13T12:00:00Z',
          },
        ]),
      ),
    )

    renderPagina()

    expect(await screen.findByText('Emitida, falta despachar')).toBeInTheDocument()
    expect(screen.getByText('Despachada')).toBeInTheDocument()
  })

  it('avisa cuando todavía no hay órdenes', async () => {
    server.use(http.get('*/api/ordenes-compra', () => HttpResponse.json([])))

    renderPagina()

    expect(await screen.findByText('Todavía no tenés órdenes de compra.')).toBeInTheDocument()
  })
})
