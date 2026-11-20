import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { reiniciarLicitacionesMock } from '../mocks/handlers'
import { server } from '../mocks/server'
import { LicitacionesComprasPage } from './LicitacionesComprasPage'

const LIC_ABIERTA_ID = 'b1b2c3d4-1111-0000-0000-000000000001'

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
  return screen.findByRole('region', { name: 'Importancia de cada criterio' })
}

function escribir(campo: HTMLElement, valor: string) {
  return userEvent.clear(campo).then(() => (valor ? userEvent.type(campo, valor) : undefined))
}

describe('LicitacionesComprasPage: ponderaciones del scoring (#156)', () => {
  beforeEach(() => {
    reiniciarLicitacionesMock()
  })

  // Los tests que escuchan pedidos (server.events.on) no deben dejar el oyente para el test siguiente.
  afterEach(() => {
    server.events.removeAllListeners()
  })

  it('muestra los pesos por defecto del servicio como porcentajes', async () => {
    renderPagina()
    const panel = await esperarPanel()

    expect(within(panel).getByLabelText('Precio (%)')).toHaveValue('50')
    expect(within(panel).getByLabelText('Flete (%)')).toHaveValue('20')
    expect(within(panel).getByLabelText('Plazo de entrega (%)')).toHaveValue('20')
    expect(within(panel).getByLabelText('Calificación del proveedor (%)')).toHaveValue('10')
    expect(within(panel).queryByRole('button', { name: 'Valores por defecto' })).not.toBeInTheDocument()
  })

  it('recalcula pidiendo al servicio los pesos elegidos y ofrece volver a los de por defecto', async () => {
    const user = userEvent.setup()
    const consultas: URLSearchParams[] = []
    server.events.on('request:start', ({ request }) => {
      const url = new URL(request.url)
      if (url.pathname === '/api/cotizaciones') consultas.push(url.searchParams)
    })

    renderPagina()
    const panel = await esperarPanel()
    await escribir(within(panel).getByLabelText('Precio (%)'), '100')
    await escribir(within(panel).getByLabelText('Flete (%)'), '0')
    await escribir(within(panel).getByLabelText('Plazo de entrega (%)'), '0')
    await escribir(within(panel).getByLabelText('Calificación del proveedor (%)'), '0')
    await user.click(within(panel).getByRole('button', { name: 'Recalcular' }))

    await waitFor(() => expect(consultas.some((q) => q.get('pesoPrecio') === '100')).toBe(true))
    const conPesos = consultas.find((q) => q.get('pesoPrecio') === '100')!
    expect(conPesos.get('pesoFlete')).toBe('0')
    expect(conPesos.get('pesoPlazo')).toBe('0')
    expect(conPesos.get('pesoCalificacion')).toBe('0')

    // El servicio normaliza: 100/0/0/0 vuelve como 1/0/0/0 y se muestra como 100 %.
    const panelNuevo = await esperarPanel()
    expect(within(panelNuevo).getByLabelText('Precio (%)')).toHaveValue('100')

    await user.click(within(panelNuevo).getByRole('button', { name: 'Valores por defecto' }))
    await waitFor(async () =>
      expect(within(await esperarPanel()).getByLabelText('Precio (%)')).toHaveValue('50'),
    )
    expect(
      within(await esperarPanel()).queryByRole('button', { name: 'Valores por defecto' }),
    ).not.toBeInTheDocument()
  })

  it('no llama al servicio si todos los pesos son 0 y explica el motivo', async () => {
    const user = userEvent.setup()
    let consultasConPesos = 0
    server.events.on('request:start', ({ request }) => {
      if (new URL(request.url).searchParams.has('pesoPrecio')) consultasConPesos++
    })

    renderPagina()
    const panel = await esperarPanel()
    for (const etiqueta of [
      'Precio (%)',
      'Flete (%)',
      'Plazo de entrega (%)',
      'Calificación del proveedor (%)',
    ]) {
      await escribir(within(panel).getByLabelText(etiqueta), '0')
    }
    await user.click(within(panel).getByRole('button', { name: 'Recalcular' }))

    expect(await within(panel).findByRole('alert')).toHaveTextContent(
      'Al menos un criterio tiene que pesar más de 0.',
    )
    expect(consultasConPesos).toBe(0)
  })

  it('avisa cuál campo está mal si escribís un texto o lo dejás vacío', async () => {
    const user = userEvent.setup()
    renderPagina()
    const panel = await esperarPanel()

    await escribir(within(panel).getByLabelText('Flete (%)'), 'abc')
    await user.click(within(panel).getByRole('button', { name: 'Recalcular' }))
    expect(await within(panel).findByRole('alert')).toHaveTextContent(
      'Flete: escribí un número de 0 o más.',
    )

    await escribir(within(panel).getByLabelText('Flete (%)'), '')
    await user.click(within(panel).getByRole('button', { name: 'Recalcular' }))
    expect(within(panel).getByRole('alert')).toHaveTextContent('Flete: escribí un número de 0 o más.')
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
    expect(within(await esperarPanel()).getByLabelText('Precio (%)')).toHaveValue('50')
  })
})
