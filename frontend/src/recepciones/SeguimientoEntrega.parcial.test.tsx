import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { server } from '../mocks/server'
import { SeguimientoEntrega } from './SeguimientoEntrega'

// Estado intermedio de la entrega (#249), según estadoEntrega del contrato de Recepciones.
describe('SeguimientoEntrega: entrega parcial (#249)', () => {
  it('muestra el estado intermedio y no dice que se recibió por completo', async () => {
    server.use(
      http.get('*/api/recepciones/ordenes/:id', ({ params }) =>
        HttpResponse.json({
          ordenCompraId: params.id,
          estadoEntrega: 'Entrega parcial / Pendiente de re-despacho',
          estaCompleta: false,
          tieneProblemaAbierto: false,
          items: [],
          recepciones: [],
        }),
      ),
    )
    render(<SeguimientoEntrega ordenCompraId="orden-1" codigo="ORDEN-1" />)

    await userEvent.setup().click(await screen.findByRole('button', { name: /Ver seguimiento/ }))
    expect(await screen.findByText('Entrega parcial.')).toBeInTheDocument()
    expect(screen.queryByText('Entrega completa.')).not.toBeInTheDocument()
  })
})
