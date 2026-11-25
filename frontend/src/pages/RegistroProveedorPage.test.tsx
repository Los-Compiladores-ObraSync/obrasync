import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { delay, http, HttpResponse } from 'msw'
import type { ReactNode } from 'react'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router'
import { beforeEach, describe, expect, it } from 'vitest'
import { setToken } from '../api/client'
import { AuthProvider } from '../auth/AuthContext'
import { STORAGE_KEY } from '../auth/sessionContext'
import { useAuth } from '../auth/useAuth'
import { reiniciarUsuariosMock } from '../mocks/handlers'
import { server } from '../mocks/server'
import { SoloSinSesion } from '../routes/SoloSinSesion'
import { RegistroProveedorPage } from './RegistroProveedorPage'

function ConRouterYSesion({ children }: { children: ReactNode }) {
  return <MemoryRouter><AuthProvider>{children}</AuthProvider></MemoryRouter>
}

function HarnessConEstado() {
  const { usuario } = useAuth()
  return usuario ? <div data-testid="usuario-logueado">{usuario.nombreCompleto} ({usuario.rol})</div> : <RegistroProveedorPage />
}

function RutaActual() {
  return <div data-testid="ruta-actual">{useLocation().pathname}</div>
}

describe('RegistroProveedorPage', () => {
  beforeEach(() => {
    localStorage.clear()
    setToken(null)
    reiniciarUsuariosMock()
  })

  it('valida los campos sin llamar a la API', async () => {
    const user = userEvent.setup()
    let llamada = false
    server.use(http.post('*/api/usuarios', () => { llamada = true; return HttpResponse.json({}) }))
    render(<ConRouterYSesion><RegistroProveedorPage /></ConRouterYSesion>)
    await user.click(screen.getByRole('button', { name: 'Crear cuenta' }))
    expect(screen.getByText('El nombre completo es obligatorio.')).toBeInTheDocument()
    expect(screen.getByText('El email es obligatorio.')).toBeInTheDocument()
    expect(screen.getByText('La contraseña es obligatoria.')).toBeInTheDocument()
    await user.type(screen.getByLabelText('Nombre completo'), 'Ana')
    await user.type(screen.getByLabelText('Email'), 'ana@empresa.com')
    await user.type(screen.getByLabelText('Contraseña'), 'abc123')
    await user.click(screen.getByRole('button', { name: 'Crear cuenta' }))
    expect(screen.getByText('La contraseña debe tener al menos 8 caracteres.')).toBeInTheDocument()
    expect(llamada).toBe(false)
  })

  it('no ofrece selector de rol', () => {
    render(<ConRouterYSesion><RegistroProveedorPage /></ConRouterYSesion>)
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument()
  })

  it('registra e inicia sesión como Proveedor', async () => {
    const user = userEvent.setup()
    render(<ConRouterYSesion><HarnessConEstado /></ConRouterYSesion>)
    await user.type(screen.getByLabelText('Nombre completo'), 'Ana López')
    await user.type(screen.getByLabelText('Email'), 'ana@empresa.com')
    await user.type(screen.getByLabelText('Contraseña'), 'clave1234')
    await user.click(screen.getByRole('button', { name: 'Crear cuenta' }))
    expect(await screen.findByTestId('usuario-logueado')).toHaveTextContent('Ana López (Proveedor)')
    expect(JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '{}')).toMatchObject({ usuario: { rol: 'Proveedor' }, token: expect.any(String) })
  })

  it('muestra el conflicto 409 y acceso al login', async () => {
    const user = userEvent.setup()
    render(<ConRouterYSesion><RegistroProveedorPage /></ConRouterYSesion>)
    await user.type(screen.getByLabelText('Nombre completo'), 'Ana')
    await user.type(screen.getByLabelText('Email'), 'proveedor@obrasync.com')
    await user.type(screen.getByLabelText('Contraseña'), 'clave1234')
    await user.click(screen.getByRole('button', { name: 'Crear cuenta' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Ya existe una cuenta con ese email')
    expect(screen.getByRole('link', { name: 'Iniciá sesión' })).toHaveAttribute('href', '/login')
  })

  it('muestra el detalle del 400', async () => {
    const user = userEvent.setup()
    server.use(http.post('*/api/usuarios', () => HttpResponse.json({ status: 400, detail: 'El nombre no es válido.' }, { status: 400 })))
    render(<ConRouterYSesion><RegistroProveedorPage /></ConRouterYSesion>)
    await user.type(screen.getByLabelText('Nombre completo'), 'Ana')
    await user.type(screen.getByLabelText('Email'), 'ana@empresa.com')
    await user.type(screen.getByLabelText('Contraseña'), 'clave1234')
    await user.click(screen.getByRole('button', { name: 'Crear cuenta' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('El nombre no es válido.')
  })

  it('muestra el mensaje genérico del 400 cuando no hay detail', async () => {
    const user = userEvent.setup()
    server.use(http.post('*/api/usuarios', () => HttpResponse.json({ status: 400 }, { status: 400 })))
    render(<ConRouterYSesion><RegistroProveedorPage /></ConRouterYSesion>)
    await user.type(screen.getByLabelText('Nombre completo'), 'Ana')
    await user.type(screen.getByLabelText('Email'), 'ana@empresa.com')
    await user.type(screen.getByLabelText('Contraseña'), 'clave1234')
    await user.click(screen.getByRole('button', { name: 'Crear cuenta' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Revisá los datos ingresados.')
  })

  it('muestra el mensaje del límite de intentos 429 aunque no haya JSON', async () => {
    const user = userEvent.setup()
    server.use(http.post('*/api/usuarios', () => new HttpResponse(null, { status: 429 })))
    render(<ConRouterYSesion><RegistroProveedorPage /></ConRouterYSesion>)
    await user.type(screen.getByLabelText('Nombre completo'), 'Ana')
    await user.type(screen.getByLabelText('Email'), 'ana@empresa.com')
    await user.type(screen.getByLabelText('Contraseña'), 'clave1234')
    await user.click(screen.getByRole('button', { name: 'Crear cuenta' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Demasiados intentos. Esperá un minuto y probá de nuevo.')
  })

  it('muestra el mensaje de conexión cuando falla la red', async () => {
    const user = userEvent.setup()
    server.use(http.post('*/api/usuarios', () => HttpResponse.error()))
    render(<ConRouterYSesion><RegistroProveedorPage /></ConRouterYSesion>)
    await user.type(screen.getByLabelText('Nombre completo'), 'Ana')
    await user.type(screen.getByLabelText('Email'), 'ana@empresa.com')
    await user.type(screen.getByLabelText('Contraseña'), 'clave1234')
    await user.click(screen.getByRole('button', { name: 'Crear cuenta' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudo conectar. Intentá de nuevo más tarde.')
  })

  it('deshabilita el botón mientras envía', async () => {
    const user = userEvent.setup()
    server.use(http.post('*/api/usuarios', async () => {
      await delay(100)
      return HttpResponse.json({ id: 'u-1', nombreCompleto: 'Ana', email: 'ana@empresa.com', token: 't', roles: ['Proveedor'] }, { status: 201 })
    }))
    render(<ConRouterYSesion><RegistroProveedorPage /></ConRouterYSesion>)
    await user.type(screen.getByLabelText('Nombre completo'), 'Ana')
    await user.type(screen.getByLabelText('Email'), 'ana@empresa.com')
    await user.type(screen.getByLabelText('Contraseña'), 'clave1234')
    const boton = screen.getByRole('button', { name: 'Crear cuenta' })
    await user.click(boton)
    expect(boton).toBeDisabled()
    expect(boton).toHaveAttribute('aria-busy', 'true')
    await waitFor(() => expect(boton).toBeEnabled())
  })

  it('redirige a / si ya hay sesión', () => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify({ usuario: { id: 'u', nombreCompleto: 'Ana', email: 'a@b.com', rol: 'Proveedor' }, token: 't' }))
    render(<MemoryRouter initialEntries={['/registro']}><AuthProvider><Routes><Route element={<SoloSinSesion />}><Route path="/registro" element={<RegistroProveedorPage />} /></Route><Route path="/" element={<RutaActual />} /></Routes></AuthProvider></MemoryRouter>)
    expect(screen.getByTestId('ruta-actual')).toHaveTextContent('/')
  })
})
