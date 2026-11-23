import { render, screen } from '@testing-library/react'
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

  it('ordena los accesos principales y mantiene visibles las Órdenes de compra', () => {
    render(
      <MemoryRouter>
        <ComprasPage />
      </MemoryRouter>,
    )

    const links = screen.getAllByRole('link')
    const textos = links.map((link) => link.textContent?.trim())
    expect(textos.indexOf('Aprobar requisiciones')).toBeLessThan(textos.indexOf('Historial de requisiciones'))
    expect(textos.indexOf('Historial de requisiciones')).toBeLessThan(textos.indexOf('Licitaciones y cotizaciones'))
    expect(textos.indexOf('Licitaciones y cotizaciones')).toBeLessThan(textos.indexOf('Órdenes de compra'))
    expect(textos.indexOf('Órdenes de compra')).toBeLessThan(textos.indexOf('Presupuesto'))
  })

  it('centraliza por licitar en el flujo y no repite indicadores en los accesos', async () => {
    server.use(http.get('*/api/licitaciones', () => HttpResponse.json([], { status: 200 })))
    render(<MemoryRouter><ComprasPage /></MemoryRouter>)
    expect(await screen.findByRole('region', { name: 'Progreso del flujo de compras' })).toBeInTheDocument()
    expect(screen.queryByRole('img', { name: /por licitar|su licitación/i })).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Historial de requisiciones' })).toHaveAttribute('href', '/compras/requisiciones/historial')
  })
})
