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

// Los botones de estado que están activos (aria-pressed="true"), en el orden en que se ven.
function estadosActivos() {
  return within(screen.getByRole('group', { name: 'Estado' }))
    .getAllByRole('button')
    .filter((b) => b.getAttribute('aria-pressed') === 'true')
    .map((b) => b.textContent)
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

  it('el enlace de edición aparece únicamente en rechazadas propias y muestra el motivo', async () => {
    renderPagina(capataz)
    const rechazada = await screen.findByText('REQ-2026-0013')
    const tarjetaRechazada = rechazada.closest('li')!
    expect(within(tarjetaRechazada).getByText(/Falta justificar el volumen/)).toBeInTheDocument()
    expect(within(tarjetaRechazada).getByRole('link', { name: 'Editar y reenviar' })).toBeInTheDocument()
    const pendiente = await screen.findByText('REQ-2026-0011')
    expect(within(pendiente.closest('li')!).queryByRole('link', { name: 'Editar y reenviar' })).not.toBeInTheDocument()
    const aprobada = screen.getByText('REQ-2026-0009')
    expect(within(aprobada.closest('li')!).queryByRole('link', { name: 'Editar y reenviar' })).not.toBeInTheDocument()
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
    // El filtro de Estado son 4 botones (#213, más Cancelada), ninguno activo al empezar.
    const botones = within(screen.getByRole('group', { name: 'Estado' })).getAllByRole('button')
    expect(botones.map((b) => b.textContent)).toEqual(['Pendiente', 'Aprobada', 'Rechazada', 'Cancelada'])
    expect(estadosActivos()).toEqual([])

    expect(await screen.findByRole('option', { name: 'Torre Alvear' })).toBeInTheDocument()
    const obras = within(screen.getByLabelText('Obra')).getAllByRole('option')
    expect(obras.map((o) => o.textContent)).toEqual(['Todas', 'Torre Alvear'])
  })

  it('al elegir un estado pide solo esas requisiciones', async () => {
    const user = userEvent.setup()
    renderPagina(comprador)
    await screen.findByText('REQ-2026-0011')

    await user.click(screen.getByRole('button', { name: 'Aprobada' }))

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

    await user.click(screen.getByRole('button', { name: 'Aprobada' }))
    await screen.findByText('No hay requisiciones con esos filtros.')
    await user.selectOptions(screen.getByLabelText('Obra'), OBRA_ID)
    await screen.findByText('No hay requisiciones con esos filtros.')

    expect(pedidos).toEqual([
      '?pagina=1&tamano=50',
      '?estado=Aprobada&pagina=1&tamano=50',
      `?estado=Aprobada&obraId=${OBRA_ID}&pagina=1&tamano=50`,
    ])
  })

  it('"Quitar filtros" está apagado sin filtros y los limpia al tocarlo', async () => {
    const user = userEvent.setup()
    renderPagina(comprador)
    await screen.findByText('REQ-2026-0011')
    const quitar = screen.getByRole('button', { name: 'Quitar filtros' })
    expect(quitar).toBeDisabled()

    await user.click(screen.getByRole('button', { name: 'Rechazada' }))
    await screen.findByText('REQ-2026-0010')
    expect(quitar).toBeEnabled()

    await user.click(quitar)

    expect(await screen.findByText('REQ-2026-0011')).toBeInTheDocument()
    expect(screen.getByText('REQ-2026-0010')).toBeInTheDocument()
    expect(estadosActivos()).toEqual([])
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

    await user.click(screen.getByRole('button', { name: 'Aprobada' }))

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

    await user.click(screen.getByRole('button', { name: 'Aprobada' }))
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'No se pudieron cargar las requisiciones.',
    )
    expect(screen.getByRole('button', { name: 'Aprobada' })).toHaveAttribute('aria-pressed', 'true')

    falla = false
    await user.click(screen.getByRole('button', { name: 'Reintentar' }))

    expect(await screen.findByText('REQ-2026-0009')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Aprobada' })).toHaveAttribute('aria-pressed', 'true')
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

    await user.click(screen.getByRole('button', { name: 'Pendiente' }))
    await user.click(screen.getByRole('button', { name: 'Aprobada' }))

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

    // La 0010 la pidió otra persona; la 0013 propia sí debe aparecer.
    await user.click(screen.getByRole('button', { name: 'Rechazada' }))

    expect(await screen.findByText('REQ-2026-0013')).toBeInTheDocument()
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

    await user.click(screen.getByRole('button', { name: 'Aprobada' }))

    const enlace = await screen.findByRole('link', { name: 'REQ-2026-0009' })
    expect(enlace).toHaveAttribute(
      'href',
      '/compras/requisiciones/historial/d1d2d3d4-0000-0000-0000-000000000009?estado=Aprobada',
    )
    expect(screen.getByTestId('ubicacion')).toHaveTextContent(
      '/compras/requisiciones/historial?estado=Aprobada',
    )
  })

  it('tocar un estado lo activa y volver a tocarlo lo desactiva y muestra todas (#213)', async () => {
    const user = userEvent.setup()
    renderPagina(comprador)
    await screen.findByText('REQ-2026-0011')

    await user.click(screen.getByRole('button', { name: 'Rechazada' }))
    await screen.findByText('REQ-2026-0010')
    expect(estadosActivos()).toEqual(['Rechazada'])
    expect(screen.queryByText('REQ-2026-0011')).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Rechazada' }))

    expect(await screen.findByText('REQ-2026-0011')).toBeInTheDocument()
    expect(estadosActivos()).toEqual([])
  })

  it('al elegir otro estado se desactiva el anterior (solo uno activo a la vez)', async () => {
    const user = userEvent.setup()
    renderPagina(comprador)
    await screen.findByText('REQ-2026-0011')

    await user.click(screen.getByRole('button', { name: 'Aprobada' }))
    await screen.findByText('REQ-2026-0009')
    await user.click(screen.getByRole('button', { name: 'Rechazada' }))
    await screen.findByText('REQ-2026-0010')

    expect(estadosActivos()).toEqual(['Rechazada'])
  })

  it('arranca con los filtros que vienen en la URL', async () => {
    renderPagina(comprador, '/compras/requisiciones/historial?estado=Aprobada')

    expect(await screen.findByText('REQ-2026-0009')).toBeInTheDocument()
    expect(screen.queryByText('REQ-2026-0011')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Aprobada' })).toHaveAttribute('aria-pressed', 'true')
  })

  it('ignora un estado inválido en la URL', async () => {
    renderPagina(comprador, '/compras/requisiciones/historial?estado=Cualquiera')

    expect(await screen.findByText('REQ-2026-0011')).toBeInTheDocument()
    expect(estadosActivos()).toEqual([])
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

describe('HistorialRequisicionesPage - paginación (#172)', () => {
  beforeEach(() => {
    reiniciarRequisicionesMock()
  })

  function requisicion(n: number) {
    return {
      requisicionId: `d1d2d3d4-0000-0000-0000-0000000001${n}`,
      numeroRequisicion: `REQ-2026-01${n}`,
      obraId: OBRA_ID,
      etapa: 'Estructura',
      solicitanteId: 'a1b2c3d4-0000-0000-0000-000000000002',
      estado: 'Pendiente',
      observaciones: null,
      fechaCreacion: '2026-10-19T12:00:00Z',
      items: [],
    }
  }

  /** Simula un servicio que entrega de a 2 filas y tiene 3 en total. */
  function tresRequisicionesDeADos(pedidos: string[] = []) {
    server.use(
      http.get('*/api/requisiciones', ({ request }) => {
        const pagina = Number(new URL(request.url).searchParams.get('pagina') ?? '1')
        pedidos.push(String(pagina))
        const filas = pagina === 1 ? [requisicion(11), requisicion(12)] : [requisicion(13)]
        return HttpResponse.json(filas, { headers: { 'X-Total-Count': '3' } })
      }),
    )
  }

  it('muestra "Ver más" cuando hay más requisiciones que las cargadas', async () => {
    tresRequisicionesDeADos()
    renderPagina(comprador)

    expect(await screen.findByText('REQ-2026-0111')).toBeInTheDocument()
    expect(screen.getByText('REQ-2026-0112')).toBeInTheDocument()
    expect(screen.queryByText('REQ-2026-0113')).not.toBeInTheDocument()
    expect(screen.getByText('Mostrando 2 de 3.')).toBeInTheDocument()
  })

  it('al tocar "Ver más" suma la página siguiente y el botón desaparece al llegar al total', async () => {
    const pedidos: string[] = []
    tresRequisicionesDeADos(pedidos)
    const user = userEvent.setup()
    renderPagina(comprador)
    await screen.findByText('REQ-2026-0111')

    await user.click(screen.getByRole('button', { name: 'Ver más' }))

    expect(await screen.findByText('REQ-2026-0113')).toBeInTheDocument()
    expect(screen.getByText('REQ-2026-0111')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Ver más' })).not.toBeInTheDocument()
    expect(pedidos).toEqual(['1', '2'])
  })

  it('no repite una requisición si la página siguiente trae una que ya estaba', async () => {
    server.use(
      http.get('*/api/requisiciones', ({ request }) => {
        const pagina = Number(new URL(request.url).searchParams.get('pagina') ?? '1')
        const filas = pagina === 1 ? [requisicion(11), requisicion(12)] : [requisicion(12), requisicion(13)]
        return HttpResponse.json(filas, { headers: { 'X-Total-Count': '3' } })
      }),
    )
    const user = userEvent.setup()
    renderPagina(comprador)
    await screen.findByText('REQ-2026-0111')

    await user.click(screen.getByRole('button', { name: 'Ver más' }))

    await screen.findByText('REQ-2026-0113')
    expect(screen.getAllByText('REQ-2026-0112')).toHaveLength(1)
  })

  it('si falla "Ver más" avisa, conserva lo cargado y deja reintentar', async () => {
    let fallar = true
    server.use(
      http.get('*/api/requisiciones', ({ request }) => {
        const pagina = Number(new URL(request.url).searchParams.get('pagina') ?? '1')
        if (pagina === 1) {
          return HttpResponse.json([requisicion(11), requisicion(12)], {
            headers: { 'X-Total-Count': '3' },
          })
        }
        if (fallar) return HttpResponse.json({ title: 'Error' }, { status: 500 })
        return HttpResponse.json([requisicion(13)], { headers: { 'X-Total-Count': '3' } })
      }),
    )
    const user = userEvent.setup()
    renderPagina(comprador)
    await screen.findByText('REQ-2026-0111')

    await user.click(screen.getByRole('button', { name: 'Ver más' }))

    expect(await screen.findByText('No se pudieron cargar más. Intentá nuevamente.')).toBeInTheDocument()
    expect(screen.getByText('REQ-2026-0111')).toBeInTheDocument()

    fallar = false
    await user.click(screen.getByRole('button', { name: 'Ver más' }))
    expect(await screen.findByText('REQ-2026-0113')).toBeInTheDocument()
  })

  it('al cambiar un filtro vuelve a la primera página', async () => {
    const pedidos: string[] = []
    server.use(
      http.get('*/api/requisiciones', ({ request }) => {
        const url = new URL(request.url)
        pedidos.push(url.search)
        return HttpResponse.json([requisicion(11), requisicion(12)], {
          headers: { 'X-Total-Count': '3' },
        })
      }),
    )
    const user = userEvent.setup()
    renderPagina(comprador)
    await screen.findByText('REQ-2026-0111')
    await user.click(screen.getByRole('button', { name: 'Ver más' }))
    await screen.findByText('Mostrando 2 de 3.')

    await user.click(screen.getByRole('button', { name: 'Aprobada' }))

    await screen.findByText('Mostrando 2 de 3.')
    expect(pedidos.at(-1)).toBe('?estado=Aprobada&pagina=1&tamano=50')
  })
})
