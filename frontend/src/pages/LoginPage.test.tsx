import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { delay, http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it } from 'vitest'
import { setToken } from '../api/client'
import { AuthProvider } from '../auth/AuthContext'
import { STORAGE_KEY } from '../auth/sessionContext'
import { useAuth } from '../auth/useAuth'
import { server } from '../mocks/server'
import { LoginPage } from './LoginPage'

function HarnessConEstado() {
  const { usuario } = useAuth()
  return (
    <>
      {usuario ? (
        <div data-testid="usuario-logueado">{usuario.nombreCompleto}</div>
      ) : (
        <LoginPage />
      )}
    </>
  )
}

describe('LoginPage', () => {
  beforeEach(() => {
    localStorage.clear()
    setToken(null)
  })

  it('no llama a la API y muestra errores inline si faltan campos', async () => {
    const user = userEvent.setup()
    let llamadaApiRealizada = false
    server.use(
      http.post('*/api/usuarios/login', () => {
        llamadaApiRealizada = true
        return HttpResponse.json({})
      }),
    )

    render(
      <AuthProvider>
        <LoginPage />
      </AuthProvider>,
    )

    await user.click(screen.getByRole('button', { name: 'Ingresar' }))

    expect(screen.getByText('El email es obligatorio.')).toBeInTheDocument()
    expect(screen.getByText('La contraseña es obligatoria.')).toBeInTheDocument()
    expect(llamadaApiRealizada).toBe(false)
  })

  it('no llama a la API y valida formato de email incorrecto', async () => {
    const user = userEvent.setup()
    let llamadaApiRealizada = false
    server.use(
      http.post('*/api/usuarios/login', () => {
        llamadaApiRealizada = true
        return HttpResponse.json({})
      }),
    )

    render(
      <AuthProvider>
        <LoginPage />
      </AuthProvider>,
    )

    await user.type(screen.getByLabelText('Email'), 'email-sin-formato')
    await user.type(screen.getByLabelText('Contraseña'), 'Test1234!')
    await user.click(screen.getByRole('button', { name: 'Ingresar' }))

    expect(screen.getByText('Ingresá un email válido.')).toBeInTheDocument()
    expect(llamadaApiRealizada).toBe(false)
  })

  it('deshabilita el botón e indica carga mientras la petición está en vuelo', async () => {
    const user = userEvent.setup()

    server.use(
      http.post('*/api/usuarios/login', async () => {
        await delay(100)
        return HttpResponse.json({
          id: 'u-1',
          nombreCompleto: 'Juan Pérez',
          email: 'capataz@obrasync.com',
          token: 'jwt-123',
          roles: ['Capataz'],
        })
      }),
    )

    render(
      <AuthProvider>
        <LoginPage />
      </AuthProvider>,
    )

    await user.type(screen.getByLabelText('Email'), 'capataz@obrasync.com')
    await user.type(screen.getByLabelText('Contraseña'), 'Test1234!')

    const boton = screen.getByRole('button', { name: 'Ingresar' })
    await user.click(boton)

    expect(boton).toBeDisabled()
    expect(boton).toHaveAttribute('aria-busy', 'true')
    expect(boton).toHaveTextContent('Ingresando…')

    await waitFor(() => {
      expect(boton).toBeEnabled()
      expect(boton).toHaveTextContent('Ingresar')
    })
  })

  it('muestra mensaje claro de credenciales inválidas ante respuesta 401', async () => {
    const user = userEvent.setup()

    render(
      <AuthProvider>
        <LoginPage />
      </AuthProvider>,
    )

    await user.type(screen.getByLabelText('Email'), 'capataz@obrasync.com')
    await user.type(screen.getByLabelText('Contraseña'), 'clave-incorrecta')
    await user.click(screen.getByRole('button', { name: 'Ingresar' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent(
      'Credenciales inválidas. Revisá tu email y contraseña.',
    )
  })

  it('muestra mensaje claro de falla de conexión ante un error de red', async () => {
    const user = userEvent.setup()
    server.use(http.post('*/api/usuarios/login', () => HttpResponse.error()))

    render(
      <AuthProvider>
        <LoginPage />
      </AuthProvider>,
    )

    await user.type(screen.getByLabelText('Email'), 'capataz@obrasync.com')
    await user.type(screen.getByLabelText('Contraseña'), 'Test1234!')
    await user.click(screen.getByRole('button', { name: 'Ingresar' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('No se pudo conectar. Intentá de nuevo más tarde.')
  })

  it('inicia la sesión y persiste en localStorage ante login exitoso', async () => {
    const user = userEvent.setup()

    render(
      <AuthProvider>
        <HarnessConEstado />
      </AuthProvider>,
    )

    await user.type(screen.getByLabelText('Email'), 'capataz@obrasync.com')
    await user.type(screen.getByLabelText('Contraseña'), 'Test1234!')
    await user.click(screen.getByRole('button', { name: 'Ingresar' }))

    await waitFor(() => {
      expect(screen.getByTestId('usuario-logueado')).toHaveTextContent(
        'Juan Pérez',
      )
    })

    const sesionGuardada = localStorage.getItem(STORAGE_KEY)
    expect(sesionGuardada).toBeTruthy()
    expect(JSON.parse(sesionGuardada!)).toMatchObject({
      usuario: {
        email: 'capataz@obrasync.com',
        nombreCompleto: 'Juan Pérez',
        rol: 'Capataz',
      },
      token: expect.any(String),
    })
  })
})
