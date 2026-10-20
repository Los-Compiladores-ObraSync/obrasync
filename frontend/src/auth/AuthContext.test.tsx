import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it } from 'vitest'
import { apiFetch, setToken } from '../api/client'
import { server } from '../mocks/server'
import { AuthProvider } from './AuthContext'
import { loginApi } from './authService'
import { STORAGE_KEY, type SesionUsuario } from './sessionContext'
import { useAuth } from './useAuth'

function VisualizadorSesion() {
  const { usuario, logout } = useAuth()
  return (
    <div>
      <div data-testid="estado-usuario">
        {usuario ? `${usuario.nombreCompleto} (${usuario.rol})` : 'Sin sesión'}
      </div>
      <button type="button" onClick={logout}>
        Cerrar
      </button>
    </div>
  )
}

describe('AuthContext - Manejo de Sesión', () => {
  beforeEach(() => {
    localStorage.clear()
    setToken(null)
  })

  it('restaura la sesión y el token en memoria desde localStorage al montar', async () => {
    const sesionPrevia = {
      usuario: {
        id: 'u-456',
        nombreCompleto: 'María López',
        email: 'maria@obrasync.com',
        rol: 'Comprador',
      } as SesionUsuario,
      token: 'jwt-guardado-previo',
    }
    localStorage.setItem(STORAGE_KEY, JSON.stringify(sesionPrevia))

    let tokenRecibido: string | null = null
    server.use(
      http.get('*/api/obras', ({ request }) => {
        tokenRecibido = request.headers.get('Authorization')
        return HttpResponse.json([])
      }),
    )

    render(
      <AuthProvider>
        <VisualizadorSesion />
      </AuthProvider>,
    )

    expect(screen.getByTestId('estado-usuario')).toHaveTextContent(
      'María López (Comprador)',
    )

    // Verifica que apiFetch envía el token restaurado
    await apiFetch('/api/obras')
    expect(tokenRecibido).toBe('Bearer jwt-guardado-previo')
  })

  it('cierra la sesión automáticamente ante un 401 en llamada autenticada (token expirado)', async () => {
    const sesionActiva = {
      usuario: {
        id: 'u-456',
        nombreCompleto: 'Carlos Ruiz',
        email: 'carlos@obrasync.com',
        rol: 'Admin',
      } as SesionUsuario,
      token: 'jwt-expirado',
    }
    localStorage.setItem(STORAGE_KEY, JSON.stringify(sesionActiva))

    server.use(
      http.get('*/api/recurso-protegido', () =>
        HttpResponse.json(
          {
            title: 'No autorizado',
            status: 401,
            detail: 'El token ha vencido.',
          },
          { status: 401 },
        ),
      ),
    )

    render(
      <AuthProvider>
        <VisualizadorSesion />
      </AuthProvider>,
    )

    expect(screen.getByTestId('estado-usuario')).toHaveTextContent(
      'Carlos Ruiz (Admin)',
    )

    // Disparamos una llamada protegida que falla con 401
    await expect(apiFetch('/api/recurso-protegido')).rejects.toThrow()

    // El hook onUnauthorized debe haber limpiado el estado y localStorage
    await waitFor(() => {
      expect(screen.getByTestId('estado-usuario')).toHaveTextContent('Sin sesión')
    })
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull()
  })

  it('no cierra sesión ni afecta el estado cuando un intento de login devuelve 401', async () => {
    // Sin sesión previa (token es null)
    render(
      <AuthProvider>
        <VisualizadorSesion />
      </AuthProvider>,
    )

    expect(screen.getByTestId('estado-usuario')).toHaveTextContent('Sin sesión')

    // El 401 ocurre durante loginApi (credenciales inválidas)
    await expect(
      loginApi({
        email: 'no-existe@obrasync.com',
        password: 'PasswordInvalida1!',
      }),
    ).rejects.toThrow()

    // Se mantiene sin sesión de forma limpia, sin llamadas espurias
    expect(screen.getByTestId('estado-usuario')).toHaveTextContent('Sin sesión')
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull()
  })

  it('permite cerrar sesión manualmente con logout()', async () => {
    const user = userEvent.setup()
    const sesion = {
      usuario: {
        id: 'u-1',
        nombreCompleto: 'Juan Pérez',
        email: 'capataz@obrasync.com',
        rol: 'Capataz',
      } as SesionUsuario,
      token: 'jwt-manual',
    }
    localStorage.setItem(STORAGE_KEY, JSON.stringify(sesion))

    render(
      <AuthProvider>
        <VisualizadorSesion />
      </AuthProvider>,
    )

    expect(screen.getByTestId('estado-usuario')).toHaveTextContent(
      'Juan Pérez (Capataz)',
    )

    await user.click(screen.getByRole('button', { name: 'Cerrar' }))

    expect(screen.getByTestId('estado-usuario')).toHaveTextContent('Sin sesión')
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull()
  })
})
