import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it } from 'vitest'
import App from './App'
import { setToken } from './api/client'
import { STORAGE_KEY } from './auth/sessionContext'
import { reiniciarRequisicionesMock } from './mocks/handlers'
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

  it('muestra la pantalla de recuperar contraseña sin necesitar sesión', () => {
    renderApp('/recuperar-contrasena')

    expect(
      screen.getByRole('heading', { level: 1, name: 'Recuperar contraseña' }),
    ).toBeInTheDocument()
  })

  it('desde el login se llega a recuperar contraseña con el enlace', async () => {
    const usuario = userEvent.setup()
    renderApp('/login')

    await usuario.click(screen.getByRole('link', { name: '¿Olvidaste tu contraseña?' }))

    expect(
      screen.getByRole('heading', { level: 1, name: 'Recuperar contraseña' }),
    ).toBeInTheDocument()
  })

  it('muestra la pantalla de elegir la nueva contraseña sin necesitar sesión', () => {
    renderApp('/restablecer-contrasena?email=capataz%40obrasync.com&token=abc123')

    expect(
      screen.getByRole('heading', { level: 1, name: 'Elegí tu nueva contraseña' }),
    ).toBeInTheDocument()
  })

  it('al elegir la nueva contraseña vuelve al login con el aviso de éxito', async () => {
    const usuario = userEvent.setup()
    renderApp('/restablecer-contrasena?email=capataz%40obrasync.com&token=abc123')

    await usuario.type(screen.getByLabelText('Nueva contraseña'), 'Nueva123!')
    await usuario.type(screen.getByLabelText('Repetí la contraseña'), 'Nueva123!')
    await usuario.click(screen.getByRole('button', { name: 'Guardar contraseña' }))

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Iniciar sesión' }),
    ).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent(
      'Tu contraseña se cambió. Ya podés iniciar sesión.',
    )
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

describe('App - alta de obra', () => {
  beforeEach(() => {
    localStorage.clear()
    setToken(null)
  })

  it('el Comprador entra a /compras/obras/nueva', () => {
    iniciarSesionComo('Comprador')
    renderApp('/compras/obras/nueva')

    expect(screen.getByRole('heading', { level: 1, name: 'Nueva obra' })).toBeInTheDocument()
  })

  it('el Admin entra a /compras/obras/nueva', () => {
    iniciarSesionComo('Admin')
    renderApp('/compras/obras/nueva')

    expect(screen.getByRole('heading', { level: 1, name: 'Nueva obra' })).toBeInTheDocument()
  })

  it('el Capataz no puede entrar: ve la pantalla de sin acceso', () => {
    iniciarSesionComo('Capataz')
    renderApp('/compras/obras/nueva')

    expect(screen.queryByRole('heading', { level: 1, name: 'Nueva obra' })).not.toBeInTheDocument()
  })

  it('sin sesión redirige al login', () => {
    renderApp('/compras/obras/nueva')

    expect(screen.getByRole('heading', { level: 1, name: 'Iniciar sesión' })).toBeInTheDocument()
  })
})

describe('App - campana de notificaciones', () => {
  beforeEach(() => {
    localStorage.clear()
    setToken(null)
    reiniciarRequisicionesMock()
  })

  it('el Comprador ve cuántas requisiciones esperan su aprobación', async () => {
    iniciarSesionComo('Comprador')
    renderApp('/')

    expect(
      await screen.findByRole('button', { name: 'Notificaciones: 2 sin leer' }),
    ).toBeInTheDocument()
  })

  it('el Capataz ve la campana sin número', () => {
    iniciarSesionComo('Capataz')
    renderApp('/')

    expect(
      screen.getByRole('button', { name: 'Notificaciones: sin avisos' }),
    ).toBeInTheDocument()
  })

  it('al aprobar una requisición el número de la campana baja', async () => {
    const usuario = userEvent.setup()
    iniciarSesionComo('Comprador')
    renderApp('/compras/requisiciones')

    await screen.findByRole('button', { name: 'Notificaciones: 2 sin leer' })
    await usuario.click(screen.getByRole('button', { name: 'Aprobar REQ-2026-0011' }))

    expect(
      await screen.findByRole('button', { name: 'Notificaciones: 1 sin leer' }),
    ).toBeInTheDocument()
  })
})

describe('App - detalle de una requisición', () => {
  beforeEach(() => {
    localStorage.clear()
    setToken(null)
    reiniciarRequisicionesMock()
  })

  it('desde el historial se abre el detalle y al volver se conserva el filtro', async () => {
    const usuario = userEvent.setup()
    iniciarSesionComo('Comprador')
    renderApp('/compras/requisiciones/historial')

    await screen.findByText('REQ-2026-0011')
    await usuario.selectOptions(screen.getByLabelText('Estado'), 'Aprobada')
    await usuario.click(await screen.findByRole('link', { name: 'REQ-2026-0009' }))

    expect(
      await screen.findByRole('heading', { level: 1, name: 'REQ-2026-0009' }),
    ).toBeInTheDocument()
    expect(screen.getByText('Obra: Torre Alvear')).toBeInTheDocument()

    await usuario.click(screen.getByRole('link', { name: '← Volver al historial' }))

    expect(await screen.findByRole('link', { name: 'REQ-2026-0009' })).toBeInTheDocument()
    expect(screen.queryByText('REQ-2026-0011')).not.toBeInTheDocument()
    expect(screen.getByLabelText('Estado')).toHaveValue('Aprobada')
  })

  it('el Capataz abre el detalle desde su historial', async () => {
    const usuario = userEvent.setup()
    localStorage.setItem(
      STORAGE_KEY,
      JSON.stringify({
        usuario: {
          id: 'a1b2c3d4-0000-0000-0000-000000000001',
          nombreCompleto: 'Juan Pérez',
          email: 'capataz@obrasync.com',
          rol: 'Capataz',
        },
        token: 'token-jwt-prueba',
      }),
    )
    renderApp('/obra/requisiciones')

    await usuario.click(await screen.findByRole('link', { name: 'REQ-2026-0011' }))

    expect(
      await screen.findByRole('heading', { level: 1, name: 'REQ-2026-0011' }),
    ).toBeInTheDocument()
    expect(screen.getByRole('link', { name: '← Volver al historial' })).toHaveAttribute(
      'href',
      '/obra/requisiciones',
    )
  })
})

describe('App - tablero de pendientes', () => {
  beforeEach(() => {
    localStorage.clear()
    setToken(null)
    reiniciarRequisicionesMock()
  })

  it('el Comprador llega desde Inicio y ve sus pendientes más antiguos primero', async () => {
    const usuario = userEvent.setup()
    iniciarSesionComo('Comprador')
    renderApp('/')

    await usuario.click(screen.getByRole('link', { name: 'Pendientes' }))

    expect(await screen.findByRole('heading', { level: 1, name: 'Pendientes' })).toBeInTheDocument()
    expect(await screen.findByText('REQ-2026-0011')).toBeInTheDocument()
    expect(screen.getByText('REQ-2026-0012')).toBeInTheDocument()
    expect(screen.getByLabelText('2 pendientes')).toBeInTheDocument()
  })

  it('desde el panel de la campana se llega al tablero', async () => {
    const usuario = userEvent.setup()
    iniciarSesionComo('Comprador')
    renderApp('/')

    await usuario.click(await screen.findByRole('button', { name: 'Notificaciones: 2 sin leer' }))
    await usuario.click(screen.getByRole('link', { name: 'Ver tablero de pendientes' }))

    expect(await screen.findByRole('heading', { level: 1, name: 'Pendientes' })).toBeInTheDocument()
  })

  it('al aprobar una desde el tablero, baja la cantidad', async () => {
    const usuario = userEvent.setup()
    iniciarSesionComo('Comprador')
    renderApp('/compras/pendientes')

    expect(await screen.findByLabelText('2 pendientes')).toBeInTheDocument()
    await usuario.click(screen.getByRole('link', { name: 'Ir a Aprobar requisiciones' }))
    await usuario.click(await screen.findByRole('button', { name: 'Aprobar REQ-2026-0011' }))
    await screen.findByRole('button', { name: 'Notificaciones: 1 sin leer' })
    await usuario.click(screen.getByRole('link', { name: '← Volver a Gestión y Compras' }))
    await usuario.click(await screen.findByRole('link', { name: 'Pendientes' }))

    expect(await screen.findByLabelText('1 pendientes')).toBeInTheDocument()
  })

  it('el Capataz no entra al tablero', async () => {
    iniciarSesionComo('Capataz')
    renderApp('/compras/pendientes')

    expect(screen.queryByRole('heading', { level: 1, name: 'Pendientes' })).not.toBeInTheDocument()
  })
})
