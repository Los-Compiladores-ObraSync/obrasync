import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter, Route, Routes } from 'react-router'
import { describe, expect, it } from 'vitest'
import type { PliegoMagicLinkDto } from '../licitaciones/magicLinkTipos'
import { server } from '../mocks/server'
import { CotizarConEnlacePage } from './CotizarConEnlacePage'

const TOKEN = 'token-de-prueba'
const URL_PLIEGO = `*/api/licitaciones/magic-links/${TOKEN}`

const pliego: PliegoMagicLinkDto = {
  licitacionId: 'b1b2c3d4-1111-0000-0000-000000000001',
  numeroRequisicion: 'REQ-0007',
  fechaLimiteCotizacion: '2099-01-10T15:00:00Z',
  fechaDeVencimientoEnlace: '2099-01-09T15:00:00Z',
  proveedorNombre: 'Corralón Norte',
  items: [
    { articuloId: 'art-1', descripcion: 'Cemento Portland', cantidad: 10, unidadMedida: 'bolsa' },
  ],
} as PliegoMagicLinkDto

function renderPagina() {
  // Sin AuthProvider a propósito: la pantalla es pública y no debe necesitar sesión.
  return render(
    <MemoryRouter initialEntries={[`/cotizar/${TOKEN}`]}>
      <Routes>
        <Route path="/cotizar/:token" element={<CotizarConEnlacePage />} />
      </Routes>
    </MemoryRouter>,
  )
}

function responderPliego() {
  server.use(http.get(URL_PLIEGO, () => HttpResponse.json(pliego)))
}

async function completarYEnviar(user: ReturnType<typeof userEvent.setup>) {
  await user.type(await screen.findByLabelText(/precio unitario/i), '1500.50')
  await user.type(screen.getByLabelText(/costo de flete/i), '200')
  await user.type(screen.getByLabelText(/plazo de entrega/i), '5')
  await user.click(screen.getByRole('button', { name: /enviar cotización/i }))
}

describe('CotizarConEnlacePage', () => {
  it('muestra el pliego sin necesitar sesión', async () => {
    responderPliego()
    renderPagina()
    expect(await screen.findByRole('heading', { name: /REQ-0007/ })).toBeInTheDocument()
    expect(screen.getByText(/Corralón Norte/)).toBeInTheDocument()
    expect(screen.getByText('Cemento Portland')).toBeInTheDocument()
  })

  it.each([
    [404, /no existe/i],
    [409, /ya se usó/i],
    [400, /venció/i],
  ])('con un %s explica el problema sin mostrar el formulario', async (status, texto) => {
    server.use(
      http.get(URL_PLIEGO, () =>
        HttpResponse.json({ title: 'x', detail: 'detalle técnico interno' }, { status }),
      ),
    )
    renderPagina()
    expect(await screen.findByText(texto)).toBeInTheDocument()
    expect(screen.queryByText(/detalle técnico interno/)).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /enviar cotización/i })).not.toBeInTheDocument()
  })

  it('con una falla del servidor ofrece reintentar y carga el pliego', async () => {
    let llamadas = 0
    server.use(
      http.get(URL_PLIEGO, () =>
        ++llamadas === 1 ? HttpResponse.json({ title: 'Error' }, { status: 500 }) : HttpResponse.json(pliego),
      ),
    )
    const user = userEvent.setup()
    renderPagina()
    await user.click(await screen.findByRole('button', { name: /reintentar/i }))
    expect(await screen.findByText('Cemento Portland')).toBeInTheDocument()
  })

  it('no envía si faltan datos y marca los errores', async () => {
    responderPliego()
    let enviado = false
    server.use(
      http.post(`${URL_PLIEGO}/cotizaciones`, () => {
        enviado = true
        return HttpResponse.json({})
      }),
    )
    const user = userEvent.setup()
    renderPagina()
    await user.click(await screen.findByRole('button', { name: /enviar cotización/i }))
    expect((await screen.findAllByRole('alert')).length).toBeGreaterThan(0)
    expect(enviado).toBe(false)
  })

  it('envía la cotización y muestra la confirmación', async () => {
    responderPliego()
    let cuerpo: unknown
    server.use(
      http.post(`${URL_PLIEGO}/cotizaciones`, async ({ request }) => {
        cuerpo = await request.json()
        return HttpResponse.json({ cotizacionId: 'c1', items: [{ articuloId: 'art-1', precioUnitario: 1500.5 }] }, { status: 201 })
      }),
    )
    const user = userEvent.setup()
    renderPagina()
    await completarYEnviar(user)
    expect(await screen.findByText(/recibimos tu cotización/i)).toBeInTheDocument()
    expect(cuerpo).toEqual({
      flete: 200,
      plazoEntregaDias: 5,
      items: [{ articuloId: 'art-1', precioUnitario: 1500.5 }],
    })
  })

  it('avisa si la oferta no cubre todo lo pedido', async () => {
    responderPliego()
    server.use(
      http.post(`${URL_PLIEGO}/cotizaciones`, () =>
        HttpResponse.json(
          { cotizacionId: 'c1', coberturaParcial: true, items: [{ articuloId: 'art-1', precioUnitario: 1, coberturaParcial: true }] },
          { status: 201 },
        ),
      ),
    )
    const user = userEvent.setup()
    renderPagina()
    await completarYEnviar(user)
    expect(await screen.findByText(/no cubre todo lo pedido/i)).toBeInTheDocument()
  })

  it('si el enlace ya se usó al enviar, bloquea el formulario', async () => {
    responderPliego()
    server.use(
      http.post(`${URL_PLIEGO}/cotizaciones`, () => HttpResponse.json({ title: 'Conflicto' }, { status: 409 })),
    )
    const user = userEvent.setup()
    renderPagina()
    await completarYEnviar(user)
    expect(await screen.findByText(/ya se usó/i)).toBeInTheDocument()
    await waitFor(() => expect(screen.getByRole('button', { name: /enviar cotización/i })).toBeDisabled())
  })

  it('si falla el envío por la red, deja reintentar con los datos cargados', async () => {
    responderPliego()
    server.use(
      http.post(`${URL_PLIEGO}/cotizaciones`, () => HttpResponse.json({ title: 'Error' }, { status: 500 })),
    )
    const user = userEvent.setup()
    renderPagina()
    await completarYEnviar(user)
    expect(await screen.findByText(/no pudimos enviar tu cotización/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/precio unitario/i)).toHaveValue('1500.50')
    expect(screen.getByRole('button', { name: /enviar cotización/i })).toBeEnabled()
  })
})
