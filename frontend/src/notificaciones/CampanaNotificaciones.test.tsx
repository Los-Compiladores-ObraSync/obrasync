import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { CampanaNotificaciones } from './CampanaNotificaciones'

describe('CampanaNotificaciones', () => {
  it('sin avisos no muestra el número y lo dice para el lector de pantalla', () => {
    render(<CampanaNotificaciones cantidad={0} />)

    expect(
      screen.getByRole('button', { name: 'Notificaciones: sin avisos' }),
    ).toBeInTheDocument()
    expect(screen.queryByText('0')).not.toBeInTheDocument()
  })

  it('sin la propiedad cantidad se comporta como 0', () => {
    render(<CampanaNotificaciones />)

    expect(
      screen.getByRole('button', { name: 'Notificaciones: sin avisos' }),
    ).toBeInTheDocument()
  })

  it('con avisos muestra el contador y lo anuncia como "sin leer"', () => {
    render(<CampanaNotificaciones cantidad={3} />)

    expect(
      screen.getByRole('button', { name: 'Notificaciones: 3 sin leer' }),
    ).toBeInTheDocument()
    expect(screen.getByText('3')).toBeInTheDocument()
  })

  it('con más de 99 avisos muestra 99+', () => {
    render(<CampanaNotificaciones cantidad={150} />)

    expect(screen.getByText('99+')).toBeInTheDocument()
  })

  it('se puede llegar con el teclado (es un botón real)', () => {
    render(<CampanaNotificaciones cantidad={1} />)

    const boton = screen.getByRole('button', { name: /Notificaciones/ })
    boton.focus()
    expect(boton).toHaveFocus()
  })
})
