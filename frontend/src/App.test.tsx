import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it } from 'vitest'
import App from './App'
import { setToken } from './api/client'
import { STORAGE_KEY } from './auth/sessionContext'
import { PORTALES } from './portales/portales'

// <App /> declara las rutas pero no trae Router: acá usamos MemoryRouter,
// que simula la barra de direcciones y nos deja elegir la ruta de arranque.
function renderApp(ruta: string) {
  return render(
    <MemoryRouter initialEntries={[ruta]}>
      <App />
    </MemoryRouter>,
  )
}

function iniciarSesionComo(rol: string) {
  localStorage.setItem(
    STORAGE_KEY,
    JSON.stringify({
      usuario: {
        id: 'user-123',
        nombreCompleto: 'Juan Pérez',
        email: 'usuario@obrasync.com',
        rol,
      },
      token: 'token-jwt-prueba',
    }),
  )
}

function linksDelMenu(): string[] {
  const menu = screen.getByRole('navigation', { name: 'Menú principal' })
  return Array.from(menu.querySelectorAll('a')).map((a) => a.textContent ?? '')
}

describe('App - sin sesión', () => {
  beforeEach(() => {
    localStorage.clear()
    setToken(null)
  })

  it('muestra el login en la raíz', () => {
    renderApp('/')

    expect(
      screen.getByRole('heading', { level: 1, name: 'Iniciar sesión' }),
    ).toBeInTheDocument()
    expect(screen.getByLabelText('Email')).toBeInTheDocument()
    expect(screen.getByLabelText('Contraseña')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Ingresar' })).toBeInTheDocument()
  })

  it('redirige al login si se intenta entrar a un portal', () => {
    renderApp('/obra')

    expect(
      screen.getByRole('heading', { level: 1, name: 'Iniciar sesión' }),
    ).toBeInTheDocument()
  })

  it('después de iniciar sesión entra al inicio con el menú de su rol', async () => {
    const usuario = userEvent.setup()
    renderApp('/obra')

    await usuario.type(screen.getByLabelText('Email'), 'comprador@obrasync.com')
    await usuario.type(screen.getByLabelText('Contraseña'), 'Test1234!')
    await usuario.click(screen.getByRole('button', { name: 'Ingresar' }))

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Bienvenido, Marta Gómez' }),
    ).toBeInTheDocument()
    expect(linksDelMenu()).toEqual(['Inicio', 'Gestión y Compras'])
  })
})

describe('App - menú según el rol', () => {
  beforeEach(() => {
    localStorage.clear()
    setToken(null)
  })

  it.each([
    ['Capataz', ['Inicio', 'Obra / Terreno']],
    ['Comprador', ['Inicio', 'Gestión y Compras']],
    ['Proveedor', ['Inicio', 'Proveedores']],
    ['Admin', ['Inicio', 'Obra / Terreno', 'Gestión y Compras', 'Proveedores']],
  ])('el %s ve estos links', (rol, esperados) => {
    iniciarSesionComo(rol)
    renderApp('/')

    expect(linksDelMenu()).toEqual(esperados)
  })

  it('muestra los datos del usuario en el inicio', () => {
    iniciarSesionComo('Capataz')
    renderApp('/')

    expect(
      screen.getByRole('heading', { level: 1, name: 'Bienvenido, Juan Pérez' }),
    ).toBeInTheDocument()
    expect(screen.getByText('Capataz')).toBeInTheDocument()
    expect(screen.getByText('usuario@obrasync.com')).toBeInTheDocument()
  })

  it('un rol sin portales ve un aviso y solo el link de Inicio', () => {
    iniciarSesionComo('Desconocido')
    renderApp('/')

    expect(screen.getByRole('status')).toHaveTextContent(
      'Tu rol todavía no tiene portales asignados.',
    )
    expect(linksDelMenu()).toEqual(['Inicio'])
  })
})

describe('App - acceso a los portales', () => {
  beforeEach(() => {
    localStorage.clear()
    setToken(null)
  })

  it('el Capataz entra a su portal', () => {
    iniciarSesionComo('Capataz')
    renderApp('/obra')

    expect(
      screen.getByRole('heading', { level: 1, name: 'Obra / Terreno' }),
    ).toBeInTheDocument()
  })

  it.each(PORTALES.map((portal) => [portal.nombre, portal.ruta]))(
    'el Admin llega a "%s" por su ruta %s',
    (nombre, ruta) => {
      iniciarSesionComo('Admin')
      renderApp(ruta)

      expect(screen.getByRole('heading', { level: 1, name: nombre })).toBeInTheDocument()
    },
  )

  it('el Proveedor que escribe la dirección de otro portal ve "Sin acceso"', () => {
    iniciarSesionComo('Proveedor')
    renderApp('/obra')

    expect(
      screen.getByRole('heading', { level: 1, name: 'Sin acceso' }),
    ).toBeInTheDocument()
    expect(
      screen.queryByRole('heading', { level: 1, name: 'Obra / Terreno' }),
    ).not.toBeInTheDocument()
  })

  it('con sesión, entrar a /login lleva al inicio', () => {
    iniciarSesionComo('Capataz')
    renderApp('/login')

    expect(
      screen.getByRole('heading', { level: 1, name: 'Bienvenido, Juan Pérez' }),
    ).toBeInTheDocument()
  })

  it('una dirección que no existe lleva al inicio', () => {
    iniciarSesionComo('Capataz')
    renderApp('/esto-no-existe')

    expect(
      screen.getByRole('heading', { level: 1, name: 'Bienvenido, Juan Pérez' }),
    ).toBeInTheDocument()
  })
})

describe('App - cierre de sesión', () => {
  beforeEach(() => {
    localStorage.clear()
    setToken(null)
  })

  it('cerrar sesión desde un portal vuelve al login y borra la sesión guardada', async () => {
    const usuario = userEvent.setup()
    iniciarSesionComo('Capataz')
    renderApp('/obra')

    await usuario.click(screen.getByRole('button', { name: 'Cerrar sesión' }))

    expect(
      screen.getByRole('heading', { level: 1, name: 'Iniciar sesión' }),
    ).toBeInTheDocument()
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull()
  })
})
