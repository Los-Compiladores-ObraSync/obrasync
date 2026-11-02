import { act, fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { delay, http, HttpResponse } from 'msw'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router'
import { describe, expect, it } from 'vitest'
import { server } from '../mocks/server'
import { RestablecerContrasenaPage } from './RestablecerContrasenaPage'

// Pantalla de destino falsa: deja ver a dónde se navegó y con qué estado.
function LoginFalso() {
  const { state } = useLocation()
  return <p>LOGIN {JSON.stringify(state)}</p>
}

function renderPagina(ruta = '/restablecer-contrasena?email=capataz%40obrasync.com&token=abc123') {
  return render(
    <MemoryRouter initialEntries={[ruta]}>
      <Routes>
        <Route path="/restablecer-contrasena" element={<RestablecerContrasenaPage />} />
        <Route path="/login" element={<LoginFalso />} />
        <Route path="/recuperar-contrasena" element={<p>PEDIR ENLACE</p>} />
      </Routes>
    </MemoryRouter>,
  )
}

async function completar(
  user: ReturnType<typeof userEvent.setup>,
  password: string,
  repetir: string,
) {
  if (password) await user.type(screen.getByLabelText('Nueva contraseña'), password)
  if (repetir) await user.type(screen.getByLabelText('Repetí la contraseña'), repetir)
  await user.click(screen.getByRole('button', { name: 'Guardar contraseña' }))
}

describe('RestablecerContrasenaPage', () => {
  it('muestra la cuenta para la que se elige la contraseña', () => {
    renderPagina()

    expect(screen.getByText('Para la cuenta capataz@obrasync.com')).toBeInTheDocument()
  })

  it.each([
    ['sin email ni token', '/restablecer-contrasena'],
    ['sin token', '/restablecer-contrasena?email=capataz%40obrasync.com'],
    ['sin email', '/restablecer-contrasena?token=abc123'],
  ])('si el enlace viene incompleto (%s) avisa y ofrece pedir uno nuevo', (_caso, ruta) => {
    renderPagina(ruta)

    expect(screen.getByRole('alert')).toHaveTextContent('El enlace no es válido o está incompleto.')
    expect(screen.queryByLabelText('Nueva contraseña')).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Pedir un nuevo enlace' })).toHaveAttribute(
      'href',
      '/recuperar-contrasena',
    )
  })

  it('no llama a la API y pide las contraseñas si están vacías', async () => {
    let llamadas = 0
    server.use(
      http.post('*/api/usuarios/password/restablecer', () => {
        llamadas++
        return HttpResponse.json({})
      }),
    )
    const user = userEvent.setup()
    renderPagina()

    await completar(user, '', '')

    expect(screen.getByText('La contraseña es obligatoria.')).toBeInTheDocument()
    expect(screen.getByText('Repetí la contraseña.')).toBeInTheDocument()
    expect(llamadas).toBe(0)
  })

  it('no llama a la API si la contraseña tiene menos de 6 caracteres', async () => {
    let llamadas = 0
    server.use(
      http.post('*/api/usuarios/password/restablecer', () => {
        llamadas++
        return HttpResponse.json({})
      }),
    )
    const user = userEvent.setup()
    renderPagina()

    await completar(user, 'Ab1!', 'Ab1!')

    expect(
      screen.getByText('La contraseña debe tener al menos 6 caracteres.'),
    ).toBeInTheDocument()
    expect(llamadas).toBe(0)
  })

  it('no llama a la API si las contraseñas no coinciden', async () => {
    let llamadas = 0
    server.use(
      http.post('*/api/usuarios/password/restablecer', () => {
        llamadas++
        return HttpResponse.json({})
      }),
    )
    const user = userEvent.setup()
    renderPagina()

    await completar(user, 'Nueva123!', 'Otra1234!')

    expect(screen.getByText('Las contraseñas no coinciden.')).toBeInTheDocument()
    expect(llamadas).toBe(0)
  })

  it('envía email, token y la nueva contraseña, y vuelve al login con el aviso de éxito', async () => {
    let cuerpo: unknown
    server.use(
      http.post('*/api/usuarios/password/restablecer', async ({ request }) => {
        cuerpo = await request.json()
        return HttpResponse.json({ mensaje: 'Contraseña restablecida.' })
      }),
    )
    const user = userEvent.setup()
    // El token trae + / = codificados en la URL (%2B %2F %3D): hay que enviarlo decodificado.
    renderPagina('/restablecer-contrasena?email=capataz%40obrasync.com&token=ab%2Bc%2Fd%3D')

    await completar(user, 'Nueva123!', 'Nueva123!')

    expect(await screen.findByText(/^LOGIN/)).toHaveTextContent(
      'LOGIN {"contrasenaRestablecida":true}',
    )
    expect(cuerpo).toEqual({
      email: 'capataz@obrasync.com',
      token: 'ab+c/d=',
      nuevaPassword: 'Nueva123!',
    })
  })

  it('muestra el detalle del servidor ante un 400 y ofrece pedir un enlace nuevo', async () => {
    server.use(
      http.post('*/api/usuarios/password/restablecer', () =>
        HttpResponse.json(
          { title: 'Token inválido', status: 400, detail: 'El enlace venció. Pedí uno nuevo.' },
          { status: 400 },
        ),
      ),
    )
    const user = userEvent.setup()
    renderPagina()

    await completar(user, 'Nueva123!', 'Nueva123!')

    expect(await screen.findByRole('alert')).toHaveTextContent('El enlace venció. Pedí uno nuevo.')
    expect(screen.getByRole('link', { name: 'Pedir un nuevo enlace' })).toHaveAttribute(
      'href',
      '/recuperar-contrasena',
    )
    // Sigue en la pantalla, con lo escrito
    expect(screen.getByLabelText('Nueva contraseña')).toHaveValue('Nueva123!')
  })

  it('usa un mensaje general si el 400 no trae detalle', async () => {
    server.use(
      http.post('*/api/usuarios/password/restablecer', () =>
        HttpResponse.json({ status: 400 }, { status: 400 }),
      ),
    )
    const user = userEvent.setup()
    renderPagina()

    await completar(user, 'Nueva123!', 'Nueva123!')

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'El enlace venció o la contraseña no cumple los requisitos.',
    )
    expect(screen.getByRole('link', { name: 'Pedir un nuevo enlace' })).toBeInTheDocument()
  })

  it('avisa que no pudo conectar si falla la red, sin ofrecer un enlace nuevo', async () => {
    server.use(http.post('*/api/usuarios/password/restablecer', () => HttpResponse.error()))
    const user = userEvent.setup()
    renderPagina()

    await completar(user, 'Nueva123!', 'Nueva123!')

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'No se pudo conectar. Intentá de nuevo más tarde.',
    )
    expect(screen.queryByRole('link', { name: 'Pedir un nuevo enlace' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Guardar contraseña' })).toBeEnabled()
  })

  it('bloquea el botón mientras guarda', async () => {
    server.use(
      http.post('*/api/usuarios/password/restablecer', async () => {
        await delay(100)
        return HttpResponse.json({})
      }),
    )
    const user = userEvent.setup()
    renderPagina()

    await completar(user, 'Nueva123!', 'Nueva123!')

    expect(screen.getByRole('button', { name: 'Guardando…' })).toBeDisabled()
    expect(await screen.findByText(/^LOGIN/)).toBeInTheDocument()
  })

  it('el botón del ojo muestra y oculta las dos contraseñas', async () => {
    const user = userEvent.setup()
    renderPagina()

    const nueva = screen.getByLabelText('Nueva contraseña')
    const repetir = screen.getByLabelText('Repetí la contraseña')
    expect(nueva).toHaveAttribute('type', 'password')
    expect(repetir).toHaveAttribute('type', 'password')

    await user.click(screen.getByRole('button', { name: 'Mostrar contraseña' }))
    expect(nueva).toHaveAttribute('type', 'text')
    expect(repetir).toHaveAttribute('type', 'text')

    await user.click(screen.getByRole('button', { name: 'Mostrar contraseña' }))
    expect(nueva).toHaveAttribute('type', 'password')
    expect(repetir).toHaveAttribute('type', 'password')
  })

  it('un doble envío seguido restablece la contraseña una sola vez', async () => {
    let llamadas = 0
    server.use(
      http.post('*/api/usuarios/password/restablecer', async () => {
        llamadas++
        await delay(50)
        return HttpResponse.json({ mensaje: 'ok' })
      }),
    )
    const user = userEvent.setup()
    renderPagina()
    await user.type(screen.getByLabelText('Nueva contraseña'), 'nueva123')
    await user.type(screen.getByLabelText('Repetí la contraseña'), 'nueva123')

    const formulario = screen.getByRole('button', { name: 'Guardar contraseña' }).closest('form')!
    act(() => {
      fireEvent.submit(formulario)
      fireEvent.submit(formulario)
    })

    expect(await screen.findByText(/^LOGIN /)).toBeInTheDocument()
    expect(llamadas).toBe(1)
  })
})
