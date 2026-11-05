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
})
