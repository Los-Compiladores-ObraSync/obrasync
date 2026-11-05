import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { delay, http, HttpResponse } from 'msw'
import { MemoryRouter, useLocation } from 'react-router'
import { beforeEach, describe, expect, it } from 'vitest'
import { AuthContext, type SesionUsuario } from '../auth/sessionContext'
import { reiniciarRequisicionesMock } from '../mocks/handlers'
import { server } from '../mocks/server'
import { HistorialRequisicionesPage } from './HistorialRequisicionesPage'

const ID_CAPATAZ = 'a1b2c3d4-0000-0000-0000-000000000001'

// Muestra la URL actual para poder comprobar que los filtros viajan en ella.
function UbicacionActual() {
  const { pathname, search } = useLocation()
  return <p data-testid="ubicacion">{pathname + search}</p>
}

function renderPagina(usuario: SesionUsuario, ruta = '/compras/requisiciones/historial') {
  return render(
    <AuthContext.Provider value={{ usuario, login: () => {}, logout: () => {} }}>
      <MemoryRouter initialEntries={[ruta]}>
        <HistorialRequisicionesPage volverA="/compras" textoVolver="← Volver a Gestión y Compras" />
        <UbicacionActual />
      </MemoryRouter>
    </AuthContext.Provider>,
  )
}

const comprador: SesionUsuario = {
  id: 'a1b2c3d4-0000-0000-0000-000000000002',
  nombreCompleto: 'Marta Gómez',
  email: 'comprador@obrasync.com',
  rol: 'Comprador',
}
const capataz: SesionUsuario = {
  id: ID_CAPATAZ,
  nombreCompleto: 'Juan Pérez',
  email: 'capataz@obrasync.com',
  rol: 'Capataz',
}

describe('HistorialRequisicionesPage', () => {
  beforeEach(() => {
    reiniciarRequisicionesMock()
  })

  it('el Comprador ve las requisiciones de todos los estados con obra, etapa y estado', async () => {
    renderPagina(comprador)

    expect(await screen.findByText('REQ-2026-0011')).toBeInTheDocument()
    expect(screen.getByText('REQ-2026-0012')).toBeInTheDocument()
    expect(screen.getByText('REQ-2026-0009')).toBeInTheDocument()
    expect(screen.getByText('REQ-2026-0010')).toBeInTheDocument()

    const tarjeta = screen.getByText('REQ-2026-0009').closest('li')!
    expect(within(tarjeta).getByText('Aprobada')).toBeInTheDocument()
    // El marco de color depende de este atributo (ver el CSS de la pantalla).
    expect(tarjeta).toHaveAttribute('data-estado', 'Aprobada')
    expect(within(tarjeta).getByText(/Obra: Torre Alvear · Etapa: Fundaciones/)).toBeInTheDocument()
    expect(within(tarjeta).getByText(/Creada el 15 de octubre de 2026/)).toBeInTheDocument()
    expect(
      within(screen.getByText('REQ-2026-0010').closest('li')!).getByText('Rechazada'),
    ).toBeInTheDocument()
  })

  it('el Capataz ve solo las requisiciones que pidió él', async () => {
    renderPagina(capataz)

    expect(await screen.findByText('REQ-2026-0011')).toBeInTheDocument()
    expect(screen.getByText('REQ-2026-0009')).toBeInTheDocument()
    // La 0010 la pidió otra persona.
    expect(screen.queryByText('REQ-2026-0010')).not.toBeInTheDocument()
  })

  it('el Capataz sin requisiciones ve un mensaje a su medida', async () => {
    server.use(http.get('*/api/requisiciones', () => HttpResponse.json([])))
    renderPagina(capataz)

    expect(await screen.findByText('Todavía no hiciste ninguna requisición.')).toBeInTheDocument()
  })

  it('el Comprador sin requisiciones ve el aviso de lista vacía', async () => {
    server.use(http.get('*/api/requisiciones', () => HttpResponse.json([])))
    renderPagina(comprador)

    expect(await screen.findByText('Todavía no hay requisiciones.')).toBeInTheDocument()
  })

  it('muestra el identificador si no se pueden traer las obras', async () => {
    server.use(http.get('*/api/obras', () => HttpResponse.error()))
    renderPagina(comprador)

    expect(await screen.findByText('REQ-2026-0011')).toBeInTheDocument()
    expect(screen.getAllByText(/Obra: b1b2c3d4-0000-0000-0000-0000000000a1/).length).toBeGreaterThan(0)
  })

  it('muestra el error y Reintentar si falla la carga, y se recupera al reintentar', async () => {
    let falla = true
    server.use(
      http.get('*/api/requisiciones', () =>
        falla ? HttpResponse.error() : HttpResponse.json([]),
      ),
    )
    const user = userEvent.setup()
    renderPagina(comprador)

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'No se pudieron cargar las requisiciones.',
    )

    falla = false
    await user.click(screen.getByRole('button', { name: 'Reintentar' }))

    expect(await screen.findByText('Todavía no hay requisiciones.')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('el enlace de arriba vuelve al portal indicado', async () => {
    renderPagina(comprador)

    await screen.findByText('REQ-2026-0011')
    expect(screen.getByRole('link', { name: '← Volver a Gestión y Compras' })).toHaveAttribute(
      'href',
      '/compras',
    )
  })
})

const OBRA_ID = 'b1b2c3d4-0000-0000-0000-0000000000a1'

describe('HistorialRequisicionesPage - filtros', () => {
  beforeEach(() => {
    reiniciarRequisicionesMock()
  })

  it('ofrece los tres estados y las obras para filtrar', async () => {
    renderPagina(comprador)

    await screen.findByText('REQ-2026-0011')
    const estados = within(screen.getByLabelText('Estado')).getAllByRole('option')
    expect(estados.map((o) => o.textContent)).toEqual([
      'Todos',
      'Pendiente',
      'Aprobada',
      'Rechazada',
    ])

    expect(await screen.findByRole('option', { name: 'Torre Alvear' })).toBeInTheDocument()
    const obras = within(screen.getByLabelText('Obra')).getAllByRole('option')
    expect(obras.map((o) => o.textContent)).toEqual(['Todas', 'Torre Alvear'])
  })

  it('al elegir un estado pide solo esas requisiciones', async () => {
    const user = userEvent.setup()
    renderPagina(comprador)
    await screen.findByText('REQ-2026-0011')

    await user.selectOptions(screen.getByLabelText('Estado'), 'Aprobada')

    expect(await screen.findByText('REQ-2026-0009')).toBeInTheDocument()
    expect(screen.queryByText('REQ-2026-0011')).not.toBeInTheDocument()
    expect(screen.queryByText('REQ-2026-0010')).not.toBeInTheDocument()
  })

  it('manda los parámetros estado y obraId al elegir cada filtro', async () => {
    const pedidos: string[] = []
    server.use(
      http.get('*/api/requisiciones', ({ request }) => {
        pedidos.push(new URL(request.url).search)
        return HttpResponse.json([])
      }),
    )
    const user = userEvent.setup()
    renderPagina(comprador)
    await screen.findByText('Todavía no hay requisiciones.')
    await screen.findByRole('option', { name: 'Torre Alvear' })

    await user.selectOptions(screen.getByLabelText('Estado'), 'Aprobada')
    await screen.findByText('No hay requisiciones con esos filtros.')
    await user.selectOptions(screen.getByLabelText('Obra'), OBRA_ID)
    await screen.findByText('No hay requisiciones con esos filtros.')

    expect(pedidos).toEqual(['', '?estado=Aprobada', `?estado=Aprobada&obraId=${OBRA_ID}`])
  })

  it('"Quitar filtros" está apagado sin filtros y los limpia al tocarlo', async () => {
    const user = userEvent.setup()
    renderPagina(comprador)
    await screen.findByText('REQ-2026-0011')
    const quitar = screen.getByRole('button', { name: 'Quitar filtros' })
    expect(quitar).toBeDisabled()

    await user.selectOptions(screen.getByLabelText('Estado'), 'Rechazada')
    await screen.findByText('REQ-2026-0010')
    expect(quitar).toBeEnabled()

    await user.click(quitar)

    expect(await screen.findByText('REQ-2026-0011')).toBeInTheDocument()
    expect(screen.getByText('REQ-2026-0010')).toBeInTheDocument()
    expect(screen.getByLabelText('Estado')).toHaveValue('')
    expect(screen.getByLabelText('Obra')).toHaveValue('')
    expect(quitar).toBeDisabled()
  })

  it('avisa si no hay requisiciones con esos filtros', async () => {
    server.use(
      http.get('*/api/requisiciones', ({ request }) =>
        new URL(request.url).searchParams.get('estado')
          ? HttpResponse.json([])
          : undefined,
      ),
    )
    const user = userEvent.setup()
    renderPagina(comprador)
    await screen.findByText('REQ-2026-0011')

    await user.selectOptions(screen.getByLabelText('Estado'), 'Aprobada')

    expect(await screen.findByText('No hay requisiciones con esos filtros.')).toBeInTheDocument()
  })

  it('si falla al filtrar muestra el error y Reintentar conserva el filtro', async () => {
    let falla = true
    server.use(
      http.get('*/api/requisiciones', ({ request }) =>
        falla && new URL(request.url).searchParams.get('estado')
          ? HttpResponse.error()
          : undefined,
      ),
    )
    const user = userEvent.setup()
    renderPagina(comprador)
    await screen.findByText('REQ-2026-0011')

    await user.selectOptions(screen.getByLabelText('Estado'), 'Aprobada')
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'No se pudieron cargar las requisiciones.',
    )
    expect(screen.getByLabelText('Estado')).toHaveValue('Aprobada')

    falla = false
    await user.click(screen.getByRole('button', { name: 'Reintentar' }))

    expect(await screen.findByText('REQ-2026-0009')).toBeInTheDocument()
    expect(screen.getByLabelText('Estado')).toHaveValue('Aprobada')
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('ignora una respuesta vieja que llega tarde', async () => {
    server.use(
      http.get('*/api/requisiciones', async ({ request }) => {
        // La de "Pendiente" tarda más que la de "Aprobada": llega cuando ya se eligió otra.
        if (new URL(request.url).searchParams.get('estado') === 'Pendiente') {
          await delay(150)
        }
        return undefined
      }),
    )
    const user = userEvent.setup()
    renderPagina(comprador)
    await screen.findByText('REQ-2026-0011')

    await user.selectOptions(screen.getByLabelText('Estado'), 'Pendiente')
    await user.selectOptions(screen.getByLabelText('Estado'), 'Aprobada')

    expect(await screen.findByText('REQ-2026-0009')).toBeInTheDocument()
    await act(async () => {
      await new Promise((resolver) => setTimeout(resolver, 300))
    })
    expect(screen.getByText('REQ-2026-0009')).toBeInTheDocument()
    expect(screen.queryByText('REQ-2026-0011')).not.toBeInTheDocument()
  })

  it('el Capataz sigue viendo solo las suyas aunque filtre', async () => {
    const user = userEvent.setup()
    renderPagina(capataz)
    await screen.findByText('REQ-2026-0011')

    // La única rechazada (REQ-2026-0010) la pidió otra persona.
    await user.selectOptions(screen.getByLabelText('Estado'), 'Rechazada')

    expect(await screen.findByText('No hay requisiciones con esos filtros.')).toBeInTheDocument()
    expect(screen.queryByText('REQ-2026-0010')).not.toBeInTheDocument()
  })
})

describe('enlaces al detalle y filtros en la URL', () => {
  beforeEach(() => {
    reiniciarRequisicionesMock()
  })

  it('el número de cada requisición lleva a su detalle', async () => {
    renderPagina(comprador)

    const enlace = await screen.findByRole('link', { name: 'REQ-2026-0009' })
    expect(enlace).toHaveAttribute(
      'href',
      '/compras/requisiciones/historial/d1d2d3d4-0000-0000-0000-000000000009',
    )
  })

  it('el enlace al detalle lleva los filtros elegidos', async () => {
    const user = userEvent.setup()
    renderPagina(comprador)
    await screen.findByText('REQ-2026-0011')

    await user.selectOptions(screen.getByLabelText('Estado'), 'Aprobada')

    const enlace = await screen.findByRole('link', { name: 'REQ-2026-0009' })
    expect(enlace).toHaveAttribute(
      'href',
      '/compras/requisiciones/historial/d1d2d3d4-0000-0000-0000-000000000009?estado=Aprobada',
    )
    expect(screen.getByTestId('ubicacion')).toHaveTextContent(
      '/compras/requisiciones/historial?estado=Aprobada',
    )
  })

  it('arranca con los filtros que vienen en la URL', async () => {
    renderPagina(comprador, '/compras/requisiciones/historial?estado=Aprobada')

    expect(await screen.findByText('REQ-2026-0009')).toBeInTheDocument()
    expect(screen.queryByText('REQ-2026-0011')).not.toBeInTheDocument()
    expect(screen.getByLabelText('Estado')).toHaveValue('Aprobada')
  })

  it('ignora un estado inválido en la URL', async () => {
    renderPagina(comprador, '/compras/requisiciones/historial?estado=Cualquiera')

    expect(await screen.findByText('REQ-2026-0011')).toBeInTheDocument()
    expect(screen.getByLabelText('Estado')).toHaveValue('')
  })

  it('"Quitar filtros" deja la URL limpia', async () => {
    const user = userEvent.setup()
    renderPagina(comprador, '/compras/requisiciones/historial?estado=Aprobada')
    await screen.findByText('REQ-2026-0009')

    await user.click(screen.getByRole('button', { name: 'Quitar filtros' }))

    expect(await screen.findByText('REQ-2026-0011')).toBeInTheDocument()
    expect(screen.getByTestId('ubicacion')).toHaveTextContent(/^\/compras\/requisiciones\/historial$/)
  })
})
