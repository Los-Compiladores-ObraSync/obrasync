import { act, fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { delay, http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import { server } from '../mocks/server'
import { RecuperarContrasenaPage } from './RecuperarContrasenaPage'

const MENSAJE_NEUTRO =
  'Si el email corresponde a una cuenta, te enviamos las instrucciones para restablecer tu contraseña.'

function renderPagina() {
  return render(
    <MemoryRouter>
      <RecuperarContrasenaPage />
    </MemoryRouter>,
  )
}

describe('RecuperarContrasenaPage', () => {
  it('no llama a la API y pide el email si está vacío', async () => {
    let llamadas = 0
    server.use(
      http.post('*/api/usuarios/password/recuperar', () => {
        llamadas++
        return HttpResponse.json({})
      }),
    )
    const user = userEvent.setup()
    renderPagina()

    await user.click(screen.getByRole('button', { name: 'Enviar instrucciones' }))

    expect(screen.getByText('El email es obligatorio.')).toBeInTheDocument()
    expect(llamadas).toBe(0)
  })

  it('no llama a la API si el email tiene un formato inválido', async () => {
    let llamadas = 0
    server.use(
      http.post('*/api/usuarios/password/recuperar', () => {
        llamadas++
        return HttpResponse.json({})
      }),
    )
    const user = userEvent.setup()
    renderPagina()

    await user.type(screen.getByLabelText('Email'), 'email-sin-formato')
    await user.click(screen.getByRole('button', { name: 'Enviar instrucciones' }))

    expect(screen.getByText('Ingresá un email válido.')).toBeInTheDocument()
    expect(llamadas).toBe(0)
  })

  it('envía el email sin espacios y muestra el mensaje neutro', async () => {
    let cuerpo: unknown
    server.use(
      http.post('*/api/usuarios/password/recuperar', async ({ request }) => {
        cuerpo = await request.json()
        return HttpResponse.json({ mensaje: 'Solicitud procesada.' })
      }),
    )
    const user = userEvent.setup()
    renderPagina()

    await user.type(screen.getByLabelText('Email'), '  capataz@obrasync.com ')
    await user.click(screen.getByRole('button', { name: 'Enviar instrucciones' }))

    expect(await screen.findByRole('status')).toHaveTextContent(MENSAJE_NEUTRO)
    expect(cuerpo).toEqual({ email: 'capataz@obrasync.com' })
    // Ya no se ve el formulario
    expect(screen.queryByLabelText('Email')).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Volver a iniciar sesión' })).toHaveAttribute(
      'href',
      '/login',
    )
  })

  it('muestra el mismo mensaje con una cuenta que existe y con una que no (no revela cuentas)', async () => {
    const user = userEvent.setup()
    renderPagina()

    // El mock responde igual para cualquier email válido, como el backend real.
    await user.type(screen.getByLabelText('Email'), 'no-existe@obrasync.com')
    await user.click(screen.getByRole('button', { name: 'Enviar instrucciones' }))

    expect(await screen.findByRole('status')).toHaveTextContent(MENSAJE_NEUTRO)
  })

  it('bloquea el botón mientras envía', async () => {
    server.use(
      http.post('*/api/usuarios/password/recuperar', async () => {
        await delay(100)
        return HttpResponse.json({})
      }),
    )
    const user = userEvent.setup()
    renderPagina()

    await user.type(screen.getByLabelText('Email'), 'capataz@obrasync.com')
    await user.click(screen.getByRole('button', { name: 'Enviar instrucciones' }))

    const boton = screen.getByRole('button', { name: 'Enviando…' })
    expect(boton).toBeDisabled()
    expect(await screen.findByRole('status')).toBeInTheDocument()
  })

  it('muestra el detalle del servidor ante un 400 y conserva el formulario', async () => {
    server.use(
      http.post('*/api/usuarios/password/recuperar', () =>
        HttpResponse.json(
          { title: 'Datos inválidos', status: 400, detail: 'El email no es válido.' },
          { status: 400 },
        ),
      ),
    )
    const user = userEvent.setup()
    renderPagina()

    await user.type(screen.getByLabelText('Email'), 'capataz@obrasync.com')
    await user.click(screen.getByRole('button', { name: 'Enviar instrucciones' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('El email no es válido.')
    expect(screen.getByLabelText('Email')).toHaveValue('capataz@obrasync.com')
  })

  it('avisa que no pudo conectar si falla la red y deja reintentar', async () => {
    server.use(http.post('*/api/usuarios/password/recuperar', () => HttpResponse.error()))
    const user = userEvent.setup()
    renderPagina()

    await user.type(screen.getByLabelText('Email'), 'capataz@obrasync.com')
    await user.click(screen.getByRole('button', { name: 'Enviar instrucciones' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'No se pudo conectar. Intentá de nuevo más tarde.',
    )
    expect(screen.getByRole('button', { name: 'Enviar instrucciones' })).toBeEnabled()
  })

  it('tiene un enlace para volver al login', () => {
    renderPagina()

    expect(screen.getByRole('link', { name: 'Volver a iniciar sesión' })).toHaveAttribute(
      'href',
      '/login',
    )
  })

  it('un doble envío seguido pide las instrucciones una sola vez', async () => {
    let llamadas = 0
    server.use(
      http.post('*/api/usuarios/password/recuperar', async () => {
        llamadas++
        await delay(50)
        return HttpResponse.json({ mensaje: 'ok' })
      }),
    )
    const user = userEvent.setup()
    renderPagina()
    await user.type(screen.getByLabelText('Email'), 'capataz@obrasync.com')

    const formulario = screen.getByRole('button', { name: 'Enviar instrucciones' }).closest('form')!
    act(() => {
      fireEvent.submit(formulario)
      fireEvent.submit(formulario)
    })

    expect(await screen.findByText(MENSAJE_NEUTRO)).toBeInTheDocument()
    expect(llamadas).toBe(1)
  })
})
