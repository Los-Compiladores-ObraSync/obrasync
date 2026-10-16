import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import App from './App'

describe('App', () => {
  it('muestra la marca en el encabezado', () => {
    render(<App />)
    expect(screen.getByRole('banner')).toHaveTextContent('OBRA SYNC')
  })

  it('muestra el título principal', () => {
    render(<App />)
    expect(
      screen.getByRole('heading', { level: 1, name: 'ObraSync' }),
    ).toBeInTheDocument()
  })
})
