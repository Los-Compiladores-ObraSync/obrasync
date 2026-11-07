import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter, Route, Routes } from 'react-router'
import { describe, expect, it } from 'vitest'
import { server } from '../mocks/server'
import { EditarObraPage } from './EditarObraPage'
import { ObrasPage } from './ObrasPage'

const ID = 'b1b2c3d4-0000-0000-0000-0000000000a1'

function renderEdicion(id = ID) {
  return render(
    <MemoryRouter initialEntries={[`/compras/obras/${id}/editar`]}>
      <Routes>
        <Route path="/compras/obras" element={<ObrasPage />} />
        <Route path="/compras/obras/:id/editar" element={<EditarObraPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('EditarObraPage', () => {
  it('carga los datos de la obra en el formulario, sin plantillas ni etapas', async () => {
    renderEdicion()

    expect(await screen.findByRole('heading', { level: 1, name: 'Editar obra' })).toBeInTheDocument()
    expect(screen.getByLabelText('Nombre de la obra')).toHaveValue('Torre Alvear')
    expect(screen.getByLabelText('Ubicación')).toHaveValue('Av. Libertador 1200')
    expect(screen.getByLabelText('Fecha de inicio')).toHaveValue('2026-10-01')
    expect(screen.queryByLabelText('Etapa 1')).not.toBeInTheDocument()
    expect(screen.getByText(/no se editan desde esta pantalla/)).toBeInTheDocument()
  })

  it('guarda los cambios y después se ve el nuevo nombre en el listado', async () => {
    const user = userEvent.setup()
    renderEdicion()

    const nombre = await screen.findByLabelText('Nombre de la obra')
    await user.clear(nombre)
    await user.type(nombre, 'Torre Alvear II')
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }))

    expect(await screen.findByText('Obra actualizada correctamente.')).toBeInTheDocument()

    await user.click(screen.getByRole('link', { name: '← Volver a obras' }))
    expect(
      await screen.findByRole('heading', { level: 2, name: 'Torre Alvear II' }),
    ).toBeInTheDocument()
  })

  it('muestra el error 409 cuando el nombre ya existe', async () => {
    const user = userEvent.setup()
    renderEdicion()

    const nombre = await screen.findByLabelText('Nombre de la obra')
    await user.clear(nombre)
    await user.type(nombre, 'Obra Duplicada')
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/ya existe una obra/i)
  })

  it('avisa si la obra no existe', async () => {
    renderEdicion('00000000-0000-0000-0000-000000000000')

    expect(await screen.findByRole('alert')).toHaveTextContent('La obra no existe o fue dada de baja.')
  })

  it('avisa si el servidor falla al cargar', async () => {
    server.use(http.get('*/api/obras', () => HttpResponse.json({}, { status: 500 })))
    renderEdicion()

    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudo cargar la obra.')
  })
})
