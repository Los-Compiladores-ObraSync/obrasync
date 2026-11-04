import { act, fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { delay, http, HttpResponse } from 'msw'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { setToken } from '../api/client'
import { server } from '../mocks/server'
import { CambiarContrasenaPage } from './CambiarContrasenaPage'

// Contraseña actual que el mock del backend acepta para todos los usuarios simulados.
const ACTUAL_OK = 'Test1234!'
const RUTA = '*/api/usuarios/cambiar-password'

function renderPagina() {
  return render(
    <MemoryRouter initialEntries={['/cambiar-contrasena']}>
      <Routes>
        <Route path="/cambiar-contrasena" element={<CambiarContrasenaPage />} />
        <Route path="/" element={<p>INICIO</p>} />
      </Routes>
    </MemoryRouter>,
  )
}

async function completar(
  user: ReturnType<typeof userEvent.setup>,
  actual: string,
  nueva: string,
  repetir: string,
) {
  if (actual) await user.type(screen.getByLabelText('Contraseña actual'), actual)
  if (nueva) await user.type(screen.getByLabelText('Nueva contraseña'), nueva)
  if (repetir) await user.type(screen.getByLabelText('Repetí la nueva contraseña'), repetir)
  await user.click(screen.getByRole('button', { name: 'Cambiar contraseña' }))
}

describe('CambiarContrasenaPage', () => {
  // El endpoint real exige sesión: el mock también, así que se simula un token activo.
  beforeEach(() => setToken('token-jwt-prueba'))
  afterEach(() => setToken(null))

  it('muestra los tres campos, el botón y el enlace para volver', () => {
    renderPagina()

    expect(screen.getByRole('heading', { level: 1, name: 'Cambiar contraseña' })).toBeInTheDocument()
    expect(screen.getByLabelText('Contraseña actual')).toBeInTheDocument()
    expect(screen.getByLabelText('Nueva contraseña')).toBeInTheDocument()
    expect(screen.getByLabelText('Repetí la nueva contraseña')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Cambiar contraseña' })).toBeEnabled()
    expect(screen.getByRole('link', { name: '← Volver al inicio' })).toHaveAttribute('href', '/')
  })

  it('no llama a la API y pide los tres campos si están vacíos', async () => {
    let llamadas = 0
    server.use(
      http.post(RUTA, () => {
        llamadas++
        return HttpResponse.json({})
      }),
    )
    const user = userEvent.setup()
    renderPagina()

    await completar(user, '', '', '')

    expect(screen.getByText('Ingresá tu contraseña actual.')).toBeInTheDocument()
    expect(screen.getByText('La nueva contraseña es obligatoria.')).toBeInTheDocument()
    expect(screen.getByText('Repetí la nueva contraseña.')).toBeInTheDocument()
    expect(llamadas).toBe(0)
  })

  it('no llama a la API si la nueva contraseña tiene menos de 6 caracteres', async () => {
    let llamadas = 0
    server.use(
      http.post(RUTA, () => {
        llamadas++
        return HttpResponse.json({})
      }),
    )
    const user = userEvent.setup()
    renderPagina()

    await completar(user, ACTUAL_OK, 'abc12', 'abc12')

    expect(screen.getByText('La contraseña debe tener al menos 6 caracteres.')).toBeInTheDocument()
    expect(llamadas).toBe(0)
  })

  it('no llama a la API si la nueva contraseña es igual a la actual', async () => {
    let llamadas = 0
    server.use(
      http.post(RUTA, () => {
        llamadas++
        return HttpResponse.json({})
      }),
    )
    const user = userEvent.setup()
    renderPagina()

    await completar(user, ACTUAL_OK, ACTUAL_OK, ACTUAL_OK)

    expect(
      screen.getByText('La nueva contraseña tiene que ser distinta de la actual.'),
    ).toBeInTheDocument()
    expect(llamadas).toBe(0)
  })

  it('no llama a la API si la confirmación no coincide', async () => {
    let llamadas = 0
    server.use(
      http.post(RUTA, () => {
        llamadas++
        return HttpResponse.json({})
      }),
    )
    const user = userEvent.setup()
    renderPagina()

    await completar(user, ACTUAL_OK, 'Nueva123!', 'Otra1234!')

    expect(screen.getByText('Las contraseñas no coinciden.')).toBeInTheDocument()
    expect(llamadas).toBe(0)
  })

  it('manda las tres contraseñas, avisa el éxito y limpia los campos', async () => {
    let cuerpo: unknown = null
    server.use(
      http.post(RUTA, async ({ request }) => {
        cuerpo = await request.json()
        return HttpResponse.json({ mensaje: 'Contraseña actualizada exitosamente.' })
      }),
    )
    const user = userEvent.setup()
    renderPagina()

    await completar(user, ACTUAL_OK, 'Nueva123!', 'Nueva123!')

    expect(await screen.findByRole('status')).toHaveTextContent('Tu contraseña se cambió correctamente.')
    expect(cuerpo).toEqual({
      passwordActual: ACTUAL_OK,
      nuevaPassword: 'Nueva123!',
      confirmarPassword: 'Nueva123!',
    })
    expect(screen.getByLabelText('Contraseña actual')).toHaveValue('')
    expect(screen.getByLabelText('Nueva contraseña')).toHaveValue('')
    expect(screen.getByLabelText('Repetí la nueva contraseña')).toHaveValue('')
    expect(screen.getByRole('button', { name: 'Cambiar contraseña' })).toBeEnabled()
  })

  it('con la contraseña actual incorrecta muestra el motivo del servidor y conserva lo escrito', async () => {
    const user = userEvent.setup()
    renderPagina()

    await completar(user, 'Equivocada1', 'Nueva123!', 'Nueva123!')

    expect(await screen.findByRole('alert')).toHaveTextContent('La contraseña actual es incorrecta.')
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
    // Se conservan los campos para que el usuario corrija solo lo que falló.
    expect(screen.getByLabelText('Nueva contraseña')).toHaveValue('Nueva123!')
    expect(screen.getByRole('button', { name: 'Cambiar contraseña' })).toBeEnabled()
  })

  it('si el servidor falla (500) avisa que no se pudo conectar', async () => {
    server.use(http.post(RUTA, () => HttpResponse.json({}, { status: 500 })))
    const user = userEvent.setup()
    renderPagina()

    await completar(user, ACTUAL_OK, 'Nueva123!', 'Nueva123!')

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'No se pudo conectar. Intentá de nuevo más tarde.',
    )
  })

  it('un nuevo intento con datos inválidos saca el aviso de éxito anterior', async () => {
    const user = userEvent.setup()
    renderPagina()
    await completar(user, ACTUAL_OK, 'Nueva123!', 'Nueva123!')
    expect(await screen.findByRole('status')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Cambiar contraseña' }))

    expect(screen.queryByRole('status')).not.toBeInTheDocument()
    expect(screen.getByText('Ingresá tu contraseña actual.')).toBeInTheDocument()
  })

  it('el ojo muestra y oculta las tres contraseñas a la vez', async () => {
    const user = userEvent.setup()
    renderPagina()
    const campos = [
      screen.getByLabelText('Contraseña actual'),
      screen.getByLabelText('Nueva contraseña'),
      screen.getByLabelText('Repetí la nueva contraseña'),
    ]
    campos.forEach((campo) => expect(campo).toHaveAttribute('type', 'password'))

    await user.click(screen.getByRole('button', { name: 'Mostrar contraseñas' }))
    campos.forEach((campo) => expect(campo).toHaveAttribute('type', 'text'))

    await user.click(screen.getByRole('button', { name: 'Mostrar contraseñas' }))
    campos.forEach((campo) => expect(campo).toHaveAttribute('type', 'password'))
  })

  it('un doble envío seguido hace una sola llamada', async () => {
    let llamadas = 0
    server.use(
      http.post(RUTA, async () => {
        llamadas++
        await delay(50)
        return HttpResponse.json({ mensaje: 'Contraseña actualizada exitosamente.' })
      }),
    )
    const user = userEvent.setup()
    renderPagina()
    await user.type(screen.getByLabelText('Contraseña actual'), ACTUAL_OK)
    await user.type(screen.getByLabelText('Nueva contraseña'), 'Nueva123!')
    await user.type(screen.getByLabelText('Repetí la nueva contraseña'), 'Nueva123!')
    const formulario = screen.getByRole('button', { name: 'Cambiar contraseña' }).closest('form')!

    // Los dos envíos dentro del mismo act: el segundo llega antes de que React vuelva a dibujar,
    // así que solo lo frena el guard inmediato (useRef), no el botón deshabilitado.
    act(() => {
      fireEvent.submit(formulario)
      fireEvent.submit(formulario)
    })

    expect(await screen.findByRole('status')).toBeInTheDocument()
    expect(llamadas).toBe(1)
  })
})
