import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import { server } from '../mocks/server'
import { NuevaObraPage } from './NuevaObraPage'

const URL_OBRAS = 'http://localhost:5000/api/obras'
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
  await user.type(screen.getByLabelText('Capataz (identificador)'), CAPATAZ_ID)
  await user.type(screen.getByLabelText('Etapa 1'), 'Cimientos')
}

describe('NuevaObraPage', () => {
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
})
