import { render, screen, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import { server } from '../mocks/server'
import { ComprasPage } from './ComprasPage'

describe('ComprasPage', () => {
  it('contiene el enlace a Licitaciones y cotizaciones apuntando a /compras/licitaciones', () => {
    render(
      <MemoryRouter>
        <ComprasPage />
      </MemoryRouter>,
    )

    const link = screen.getByRole('link', { name: 'Licitaciones y cotizaciones' })
    expect(link).toBeInTheDocument()
    expect(link).toHaveAttribute('href', '/compras/licitaciones')
  })

  it('contiene el enlace a Presupuesto apuntando a /compras/presupuesto', () => {
    render(
      <MemoryRouter>
        <ComprasPage />
      </MemoryRouter>,
    )

    const link = screen.getByRole('link', { name: 'Presupuesto' })
    expect(link).toBeInTheDocument()
    expect(link).toHaveAttribute('href', '/compras/presupuesto')
  })

  it('contiene el enlace a Órdenes de compra apuntando a /compras/ordenes', () => {
    render(
      <MemoryRouter>
        <ComprasPage />
      </MemoryRouter>,
    )

    expect(screen.getByRole('link', { name: 'Órdenes de compra' })).toHaveAttribute(
      'href',
      '/compras/ordenes',
    )
  })

  it('contiene el enlace a Cargar insumos (CSV) apuntando a /compras/insumos/carga', () => {
    render(
      <MemoryRouter>
        <ComprasPage />
      </MemoryRouter>,
    )

    expect(screen.getByRole('link', { name: 'Cargar insumos (CSV)' })).toHaveAttribute(
      'href',
      '/compras/insumos/carga',
    )
  })

  it('tiene una salida al inicio (la pantalla no queda sin forma de volver)', () => {
    render(
      <MemoryRouter>
        <ComprasPage />
      </MemoryRouter>,
    )

    expect(screen.getByRole('link', { name: 'Volver al inicio' })).toHaveAttribute('href', '/')
  })

  it('el Historial de requisiciones muestra cuántas aprobadas esperan su licitación', async () => {
    // Sin ninguna licitación publicada, todas las requisiciones aprobadas están por licitar.
    server.use(http.get('*/api/licitaciones', () => HttpResponse.json([], { status: 200 })))
    render(
      <MemoryRouter>
        <ComprasPage />
      </MemoryRouter>,
    )

    const historial = screen.getByRole('link', { name: /Historial de requisiciones/ })
    expect(await within(historial).findByRole('img', { name: /su licitación/ })).toBeInTheDocument()
  })
})
