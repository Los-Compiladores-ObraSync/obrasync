import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import App from './App'
import { setToken } from './api/client'
import { STORAGE_KEY } from './auth/sessionContext'

describe('App', () => {
  beforeEach(() => {
    localStorage.clear()
    setToken(null)
  })

  it('muestra el formulario de login cuando no hay sesión iniciada', () => {
    render(<App />)

    expect(
      screen.getByRole('heading', { level: 1, name: 'Iniciar sesión' }),
    ).toBeInTheDocument()
    expect(screen.getByLabelText('Email')).toBeInTheDocument()
    expect(screen.getByLabelText('Contraseña')).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Ingresar' }),
    ).toBeInTheDocument()
  })

  it('muestra la pantalla de inicio con datos y botón de cierre cuando hay sesión', () => {
    const sesionMock = {
      usuario: {
        id: 'user-123',
        nombreCompleto: 'Juan Pérez',
        email: 'capataz@obrasync.com',
        rol: 'Capataz',
      },
      token: 'token-jwt-prueba',
    }
    localStorage.setItem(STORAGE_KEY, JSON.stringify(sesionMock))

    render(<App />)

    expect(
      screen.getByRole('heading', { level: 1, name: 'Bienvenido, Juan Pérez' }),
    ).toBeInTheDocument()
    expect(screen.getByText('Capataz')).toBeInTheDocument()
    expect(screen.getByText('capataz@obrasync.com')).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Cerrar sesión' }),
    ).toBeInTheDocument()
  })
})

