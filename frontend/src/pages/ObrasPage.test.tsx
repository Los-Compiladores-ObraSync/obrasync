import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import { server } from '../mocks/server'
import { ObrasPage } from './ObrasPage'

function renderPagina() {
  return render(
    <MemoryRouter>
      <ObrasPage />
    </MemoryRouter>,
  )
}

describe('ObrasPage', () => {
  it('lista las obras con ubicación, capataz y etapas', async () => {
    renderPagina()

    const titulo = await screen.findByRole('heading', { level: 2, name: 'Torre Alvear' })
    const tarjeta = titulo.closest('li')!
    expect(within(tarjeta).getByText('Ubicación: Av. Libertador 1200')).toBeInTheDocument()
    expect(within(tarjeta).getByText('Capataz: Juan Pérez')).toBeInTheDocument()

    const etapas = within(tarjeta).getByRole('list', { name: 'Etapas de Torre Alvear' })
    const filas = within(etapas).getAllByRole('listitem')
    expect(filas.map((f) => f.textContent)).toEqual([
      'FundacionesCompletada',
      'EstructuraPendiente',
      'AlbañileríaPendiente',
    ])
  })

  it('muestra "Sin asignar" y "Sin etapas cargadas" cuando faltan esos datos', async () => {
    server.use(
      http.get('*/api/obras', () =>
        HttpResponse.json([
          {
            id: 'b1b2c3d4-0000-0000-0000-0000000000b2',
            nombre: 'Edificio Norte',
            ubicacion: 'Calle 9 de Julio 55',
            fechaInicio: '2026-11-01T08:00:00Z',
            capataz: null,
            etapas: [],
          },
        ]),
      ),
    )
    renderPagina()

    expect(await screen.findByRole('heading', { level: 2, name: 'Edificio Norte' })).toBeInTheDocument()
    expect(screen.getByText('Capataz: Sin asignar')).toBeInTheDocument()
    expect(screen.getByText('Sin etapas cargadas.')).toBeInTheDocument()
  })

  it('muestra un aviso y el acceso para crear una obra si no hay ninguna', async () => {
    server.use(http.get('*/api/obras', () => HttpResponse.json([])))
    renderPagina()

    expect(await screen.findByText('Todavía no hay obras cargadas.')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Crear una obra nueva' })).toHaveAttribute(
      'href',
      '/compras/obras/nueva',
    )
  })

  it('muestra el error y Reintentar si falla la carga, y se recupera al reintentar', async () => {
    let falla = true
    server.use(
      http.get('*/api/obras', () => (falla ? HttpResponse.error() : HttpResponse.json([]))),
    )
    const user = userEvent.setup()
    renderPagina()

    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudieron cargar las obras.')

    falla = false
    await user.click(screen.getByRole('button', { name: 'Reintentar' }))

    expect(await screen.findByText('Todavía no hay obras cargadas.')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('el enlace de arriba vuelve al inicio', async () => {
    renderPagina()

    await screen.findByRole('heading', { level: 2, name: 'Torre Alvear' })
    expect(screen.getByRole('link', { name: '← Volver al inicio' })).toHaveAttribute(
      'href',
      '/',
    )
  })
})

describe('ObrasPage - editar y dar de baja (#119)', () => {
  it('cada obra tiene un enlace para editarla', async () => {
    renderPagina()

    const enlace = await screen.findByRole('link', { name: 'Editar Torre Alvear' })
    expect(enlace).toHaveAttribute(
      'href',
      '/compras/obras/b1b2c3d4-0000-0000-0000-0000000000a1/editar',
    )
  })

  it('pide confirmación y, al confirmar, saca la obra del listado', async () => {
    const user = userEvent.setup()
    renderPagina()

    await user.click(await screen.findByRole('button', { name: 'Dar de baja Torre Alvear' }))
    expect(screen.getByText(/¿Dar de baja "Torre Alvear"\?/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Sí, dar de baja' }))

    expect(await screen.findByText('La obra "Torre Alvear" se dio de baja.')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { level: 2, name: 'Torre Alvear' })).not.toBeInTheDocument()
  })

  it('cancelar no da de baja nada', async () => {
    const user = userEvent.setup()
    renderPagina()

    await user.click(await screen.findByRole('button', { name: 'Dar de baja Torre Alvear' }))
    await user.click(screen.getByRole('button', { name: 'Cancelar' }))

    expect(screen.getByRole('heading', { level: 2, name: 'Torre Alvear' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Sí, dar de baja' })).not.toBeInTheDocument()
  })

  it('si la obra ya no existe (404) avisa y la saca de la lista', async () => {
    server.use(
      http.delete('*/api/obras/:id', () =>
        HttpResponse.json({ title: 'No encontrada', status: 404 }, { status: 404 }),
      ),
    )
    const user = userEvent.setup()
    renderPagina()

    await user.click(await screen.findByRole('button', { name: 'Dar de baja Torre Alvear' }))
    await user.click(screen.getByRole('button', { name: 'Sí, dar de baja' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Esa obra ya no existe.')
    expect(screen.queryByRole('heading', { level: 2, name: 'Torre Alvear' })).not.toBeInTheDocument()
  })

  it('si no tiene permiso (403) lo avisa y deja la obra', async () => {
    server.use(
      http.delete('*/api/obras/:id', () =>
        HttpResponse.json({ title: 'Prohibido', status: 403 }, { status: 403 }),
      ),
    )
    const user = userEvent.setup()
    renderPagina()

    await user.click(await screen.findByRole('button', { name: 'Dar de baja Torre Alvear' }))
    await user.click(screen.getByRole('button', { name: 'Sí, dar de baja' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('No tenés permiso para dar de baja obras.')
    expect(screen.getByRole('heading', { level: 2, name: 'Torre Alvear' })).toBeInTheDocument()
  })
})
