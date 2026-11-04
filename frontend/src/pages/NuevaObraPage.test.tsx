import { act, fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { delay, http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import { server } from '../mocks/server'
import { NuevaObraPage } from './NuevaObraPage'

const URL_OBRAS = 'http://localhost:5000/api/obras'
const URL_CAPATACES = 'http://localhost:5000/api/usuarios/capataces'
const CAPATAZ_ID = 'a1b2c3d4-0000-0000-0000-000000000001'

function renderPagina() {
  return render(
    <MemoryRouter>
      <NuevaObraPage />
    </MemoryRouter>,
  )
}

async function completarFormulario(
  user: ReturnType<typeof userEvent.setup>,
  nombre = 'Torre Norte',
) {
  await user.type(screen.getByLabelText('Nombre de la obra'), nombre)
  await user.type(screen.getByLabelText('Ubicación'), 'Av. Siempre Viva 742')
  await user.type(screen.getByLabelText('Fecha de inicio'), '2026-11-02')
  await user.selectOptions(await screen.findByLabelText('Capataz'), CAPATAZ_ID)
  await user.type(screen.getByLabelText('Etapa 1'), 'Cimientos')
}

describe('NuevaObraPage', () => {
  it('carga los capataces en un desplegable', async () => {
    renderPagina()

    expect(await screen.findByRole('option', { name: 'Juan Pérez' })).toBeInTheDocument()
  })

  it('avisa y deja reintentar si falla la carga de capataces', async () => {
    server.use(http.get(URL_CAPATACES, () => HttpResponse.error()))
    const user = userEvent.setup()
    renderPagina()

    expect(await screen.findByText('No se pudo cargar la lista de capataces.')).toBeInTheDocument()

    server.resetHandlers()
    await user.click(screen.getByRole('button', { name: 'Reintentar' }))

    expect(await screen.findByRole('option', { name: 'Juan Pérez' })).toBeInTheDocument()
  })

  it('avisa (sin bloquear) cuando la fecha de inicio ya pasó', async () => {
    const user = userEvent.setup()
    renderPagina()

    await user.type(screen.getByLabelText('Fecha de inicio'), '2020-01-01')

    expect(
      screen.getByText('Esta fecha ya pasó. ¿La obra ya empezó? Si es así, está bien.'),
    ).toBeInTheDocument()
  })

  it('no avisa cuando la fecha de inicio es futura', async () => {
    const user = userEvent.setup()
    renderPagina()

    await user.type(screen.getByLabelText('Fecha de inicio'), '2999-12-31')

    expect(screen.queryByText(/Esta fecha ya pasó/)).not.toBeInTheDocument()
  })

  it('arranca con una etapa vacía', () => {
    renderPagina()

    expect(screen.getByLabelText('Etapa 1')).toBeInTheDocument()
    expect(screen.queryByLabelText('Etapa 2')).not.toBeInTheDocument()
  })

  it('agrega y quita etapas', async () => {
    const user = userEvent.setup()
    renderPagina()

    await user.click(screen.getByRole('button', { name: '+ Agregar etapa' }))
    expect(screen.getByLabelText('Etapa 2')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Quitar etapa 2' }))
    expect(screen.queryByLabelText('Etapa 2')).not.toBeInTheDocument()
  })

  it('al quitar una etapa del medio conserva el texto de las otras', async () => {
    const user = userEvent.setup()
    renderPagina()

    await user.type(screen.getByLabelText('Etapa 1'), 'Cimientos')
    await user.click(screen.getByRole('button', { name: '+ Agregar etapa' }))
    await user.type(screen.getByLabelText('Etapa 2'), 'Estructura')
    await user.click(screen.getByRole('button', { name: '+ Agregar etapa' }))
    await user.type(screen.getByLabelText('Etapa 3'), 'Techos')

    await user.click(screen.getByRole('button', { name: 'Quitar etapa 2' }))

    expect(screen.getByLabelText('Etapa 1')).toHaveValue('Cimientos')
    expect(screen.getByLabelText('Etapa 2')).toHaveValue('Techos')
  })

  it('muestra errores y no llama a la API si el formulario está vacío', async () => {
    let llamadas = 0
    server.use(
      http.post(URL_OBRAS, () => {
        llamadas++
        return HttpResponse.text('x')
      }),
    )
    const user = userEvent.setup()
    renderPagina()

    await user.click(screen.getByRole('button', { name: 'Crear obra' }))

    expect(screen.getByText('El nombre es obligatorio.')).toBeInTheDocument()
    expect(screen.getByText('La ubicación es obligatoria.')).toBeInTheDocument()
    expect(screen.getByText('La fecha de inicio es obligatoria.')).toBeInTheDocument()
    expect(screen.getByText('El capataz es obligatorio.')).toBeInTheDocument()
    expect(screen.getByText('El nombre de la etapa es obligatorio.')).toBeInTheDocument()
    expect(llamadas).toBe(0)
  })

  it('envía el cuerpo del contrato y avisa que se creó', async () => {
    let cuerpo: unknown = null
    server.use(
      http.post(URL_OBRAS, async ({ request }) => {
        cuerpo = await request.json()
        return HttpResponse.text('id-nuevo')
      }),
    )
    const user = userEvent.setup()
    renderPagina()

    await completarFormulario(user)
    await user.click(screen.getByRole('button', { name: '+ Agregar etapa' }))
    await user.type(screen.getByLabelText('Etapa 2'), 'Estructura')
    await user.click(screen.getByRole('button', { name: 'Crear obra' }))

    expect(await screen.findByText('Obra creada correctamente.')).toBeInTheDocument()
    expect(cuerpo).toEqual({
      nombre: 'Torre Norte',
      ubicacion: 'Av. Siempre Viva 742',
      fechaInicio: '2026-11-02T00:00:00.000Z',
      capatazId: CAPATAZ_ID,
      fasesEtapas: ['Cimientos', 'Estructura'],
    })
    // El formulario queda limpio para cargar otra obra.
    expect(screen.getByLabelText('Nombre de la obra')).toHaveValue('')
    expect(screen.getByLabelText('Etapa 1')).toHaveValue('')
  })

  it('muestra el detalle del servidor en un 409 y conserva lo escrito', async () => {
    const user = userEvent.setup()
    renderPagina()

    await completarFormulario(user, 'Obra Duplicada')
    await user.click(screen.getByRole('button', { name: 'Crear obra' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Ya existe una obra con ese nombre.')
    expect(screen.getByLabelText('Nombre de la obra')).toHaveValue('Obra Duplicada')
  })

  it('avisa que no hay permiso en un 403', async () => {
    server.use(
      http.post(URL_OBRAS, () => HttpResponse.json({ status: 403 }, { status: 403 })),
    )
    const user = userEvent.setup()
    renderPagina()

    await completarFormulario(user)
    await user.click(screen.getByRole('button', { name: 'Crear obra' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('No tenés permiso para crear obras.')
  })

  it('muestra un mensaje general si falla la red', async () => {
    server.use(http.post(URL_OBRAS, () => HttpResponse.error()))
    const user = userEvent.setup()
    renderPagina()

    await completarFormulario(user)
    await user.click(screen.getByRole('button', { name: 'Crear obra' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudo conectar')
  })

  it('un doble envío seguido crea la obra una sola vez', async () => {
    let llamadas = 0
    server.use(
      http.post(URL_OBRAS, async () => {
        llamadas++
        await delay(50)
        return HttpResponse.text('id-nuevo')
      }),
    )
    const user = userEvent.setup()
    renderPagina()
    await completarFormulario(user)

    // Dos envíos en el mismo instante (doble clic, o Enter + clic): el botón todavía no se deshabilitó.
    const formulario = screen.getByRole('button', { name: 'Crear obra' }).closest('form')!
    act(() => {
      fireEvent.submit(formulario)
      fireEvent.submit(formulario)
    })

    expect(await screen.findByText('Obra creada correctamente.')).toBeInTheDocument()
    expect(llamadas).toBe(1)
  })

  it('después de un error se puede volver a enviar', async () => {
    let llamadas = 0
    server.use(
      http.post(URL_OBRAS, () => {
        llamadas++
        return llamadas === 1
          ? HttpResponse.json({ status: 500 }, { status: 500 })
          : HttpResponse.text('id-nuevo')
      }),
    )
    const user = userEvent.setup()
    renderPagina()
    await completarFormulario(user)

    await user.click(screen.getByRole('button', { name: 'Crear obra' }))
    expect(await screen.findByRole('alert')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Crear obra' }))

    expect(await screen.findByText('Obra creada correctamente.')).toBeInTheDocument()
    expect(llamadas).toBe(2)
  })
})
