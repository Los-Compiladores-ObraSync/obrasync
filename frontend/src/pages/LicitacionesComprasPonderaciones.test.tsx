import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { reiniciarLicitacionesMock } from '../mocks/handlers'
import { server } from '../mocks/server'
import { LicitacionesComprasPage } from './LicitacionesComprasPage'

const LIC_ABIERTA_ID = 'b1b2c3d4-1111-0000-0000-000000000001'
const CRITERIOS = ['Precio', 'Flete', 'Plazo de entrega', 'Calificación del proveedor']

function renderPagina() {
  return render(
    <MemoryRouter initialEntries={[`/compras/licitaciones?licitacionId=${LIC_ABIERTA_ID}`]}>
      <Routes>
        <Route path="/compras/licitaciones" element={<LicitacionesComprasPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

async function esperarPanel() {
  return screen.findByRole('region', { name: '¿Qué buscas en una oferta?' })
}

function grupo(panel: HTMLElement, criterio: string) {
  return within(panel).getByRole('group', { name: criterio })
}

/** Cuántas estrellas tiene elegidas un criterio: la que está marcada (aria-pressed), o 0. */
function estrellasElegidas(panel: HTMLElement, criterio: string): number {
  const marcada = within(grupo(panel, criterio))
    .getAllByRole('button')
    .find((b) => b.getAttribute('aria-pressed') === 'true')
  return marcada ? Number(marcada.getAttribute('aria-label')!.split(' ')[0]) : 0
}

async function elegir(panel: HTMLElement, criterio: string, estrellas: number) {
  await userEvent.click(within(grupo(panel, criterio)).getByRole('button', { name: `${estrellas} de 5` }))
}

describe('LicitacionesComprasPage: ponderaciones del scoring (#156)', () => {
  beforeEach(() => {
    reiniciarLicitacionesMock()
  })

  // Los tests que escuchan pedidos (server.events.on) no deben dejar el oyente para el test siguiente.
  afterEach(() => {
    server.events.removeAllListeners()
  })

  it('muestra los pesos por defecto del servicio como estrellas (5, 2, 2 y 1)', async () => {
    renderPagina()
    const panel = await esperarPanel()

    expect(CRITERIOS.map((c) => estrellasElegidas(panel, c))).toEqual([5, 2, 2, 1])
    expect(within(panel).queryByRole('button', { name: 'Valores por defecto' })).not.toBeInTheDocument()
  })

  it('recalcula pidiendo al servicio las estrellas elegidas y ofrece volver a los de por defecto', async () => {
    const user = userEvent.setup()
    const consultas: URLSearchParams[] = []
    server.events.on('request:start', ({ request }) => {
      const url = new URL(request.url)
      if (url.pathname === '/api/cotizaciones') consultas.push(url.searchParams)
    })

    renderPagina()
    const panel = await esperarPanel()
    // Flete, plazo y calificación en 0 (tocar la estrella elegida la apaga); precio queda en 5.
    await elegir(panel, 'Flete', 2)
    await elegir(panel, 'Plazo de entrega', 2)
    await elegir(panel, 'Calificación del proveedor', 1)
    await user.click(within(panel).getByRole('button', { name: 'Recalcular' }))

    await waitFor(() => expect(consultas.some((q) => q.get('pesoPrecio') === '5')).toBe(true))
    const conPesos = consultas.find((q) => q.get('pesoPrecio') === '5')!
    expect(conPesos.get('pesoFlete')).toBe('0')
    expect(conPesos.get('pesoPlazo')).toBe('0')
    expect(conPesos.get('pesoCalificacion')).toBe('0')

    // El servicio normaliza: 5/0/0/0 vuelve como 1/0/0/0 y se muestra como 5 estrellas en precio.
    const panelNuevo = await esperarPanel()
    expect(CRITERIOS.map((c) => estrellasElegidas(panelNuevo, c))).toEqual([5, 0, 0, 0])

    await user.click(within(panelNuevo).getByRole('button', { name: 'Valores por defecto' }))
    await waitFor(async () =>
      expect(estrellasElegidas(await esperarPanel(), 'Flete')).toBe(2),
    )
    expect(
      within(await esperarPanel()).queryByRole('button', { name: 'Valores por defecto' }),
    ).not.toBeInTheDocument()
  })

  it('se pueden cambiar las estrellas de un criterio', async () => {
    renderPagina()
    const panel = await esperarPanel()

    await elegir(panel, 'Flete', 4)
    expect(estrellasElegidas(panel, 'Flete')).toBe(4)
    await elegir(panel, 'Flete', 4)
    expect(estrellasElegidas(panel, 'Flete')).toBe(0)
  })

  it('no llama al servicio si todos tienen 0 estrellas y explica el motivo', async () => {
    const user = userEvent.setup()
    let consultasConPesos = 0
    server.events.on('request:start', ({ request }) => {
      if (new URL(request.url).searchParams.has('pesoPrecio')) consultasConPesos++
    })

    renderPagina()
    const panel = await esperarPanel()
    await elegir(panel, 'Precio', 5)
    await elegir(panel, 'Flete', 2)
    await elegir(panel, 'Plazo de entrega', 2)
    await elegir(panel, 'Calificación del proveedor', 1)
    await user.click(within(panel).getByRole('button', { name: 'Recalcular' }))

    expect(await within(panel).findByRole('alert')).toHaveTextContent(
      'Al menos un criterio debe tener una estrella.',
    )
    expect(consultasConPesos).toBe(0)
  })

  it('si el servicio rechaza los pesos (400), muestra su mensaje y vuelve a los valores por defecto', async () => {
    const user = userEvent.setup()
    server.use(
      http.get('*/api/cotizaciones', ({ request }) => {
        const url = new URL(request.url)
        if (url.searchParams.has('pesoPrecio')) {
          return HttpResponse.json(
            { title: 'Datos inválidos', status: 400, detail: 'Pesos rechazados por el servicio.' },
            { status: 400 },
          )
        }
        return HttpResponse.json([
          {
            cotizacionId: 'c1111111-0000-0000-0000-000000000001',
            licitacionId: LIC_ABIERTA_ID,
            proveedorId: 'f7a8b9c0-1111-2222-3333-444455556666',
            fechaCreacion: '2026-10-22T10:00:00Z',
            items: [],
            puntajeTotal: 50,
            esGanadora: true,
            ponderacionesScoring: { precio: 0.5, flete: 0.2, plazo: 0.2, calificacion: 0.1 },
          },
        ])
      }),
    )

    renderPagina()
    const panel = await esperarPanel()
    await user.click(within(panel).getByRole('button', { name: 'Recalcular' }))

    expect(await screen.findByText('Pesos rechazados por el servicio.')).toBeInTheDocument()
    expect(estrellasElegidas(await esperarPanel(), 'Precio')).toBe(5)
  })
})
