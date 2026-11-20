import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import { ObraPage } from './ObraPage'

describe('ObraPage', () => {
  it('muestra el título del portal de obra', () => {
    render(
      <MemoryRouter>
        <ObraPage />
      </MemoryRouter>,
    )

    expect(screen.getByRole('heading', { level: 1, name: 'Obra / Terreno' })).toBeInTheDocument()
  })

  it.each([
    ['Nueva requisición', '/obra/requisicion/nueva'],
    ['Asistente', '/obra/asistente'],
    ['Recibir mercadería', '/obra/recepcion'],
    ['Historial de requisiciones', '/obra/requisiciones'],
    ['Presupuesto', '/obra/presupuesto'],
  ])('tiene el enlace "%s" que lleva a %s', (nombre, ruta) => {
    render(
      <MemoryRouter>
        <ObraPage />
      </MemoryRouter>,
    )

    expect(screen.getByRole('link', { name: nombre })).toHaveAttribute('href', ruta)
  })
})
