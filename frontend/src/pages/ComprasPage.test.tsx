import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
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

  it('tiene una salida al inicio (la pantalla no queda sin forma de volver)', () => {
    render(
      <MemoryRouter>
        <ComprasPage />
      </MemoryRouter>,
    )

    expect(screen.getByRole('link', { name: 'Volver al inicio' })).toHaveAttribute('href', '/')
  })
})
