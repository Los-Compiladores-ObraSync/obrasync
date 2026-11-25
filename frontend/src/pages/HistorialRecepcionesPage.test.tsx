import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { expect, it, vi } from 'vitest'
import { HistorialRecepcionesPage } from './HistorialRecepcionesPage'

vi.mock('../recepciones/recepcionesService', () => ({
  obtenerHistorialRecepciones: async () => [
    { ordenCompraId: 'completa-1', estado: 3, fechaRecepcion: '2026-11-24T12:00:00Z' },
  ],
  obtenerOrdenesPendientes: async () => [
    { ordenCompraId: 'enviada-1', fechaDespacho: '2026-11-24T12:00:00Z' },
    { ordenCompraId: 'emitida-1', fechaDespacho: null },
  ],
}))
vi.mock('../requisiciones/requisicionesService', () => ({ obtenerObras: async () => [] }))
vi.mock('../recepciones/SeguimientoEntrega', () => ({ SeguimientoEntrega: () => null }))

it('incluye las órdenes despachadas sin recepción en el filtro Pendientes', async () => {
  render(<MemoryRouter><HistorialRecepcionesPage /></MemoryRouter>)

  expect(await screen.findByText('Recepción completa')).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Pendientes' }))

  expect(screen.getByText('Pendiente de recepción')).toBeInTheDocument()
  expect(screen.queryByText('Recepción completa')).not.toBeInTheDocument()
  expect(screen.queryByRole('heading', { name: /emitida/i })).not.toBeInTheDocument()
})
