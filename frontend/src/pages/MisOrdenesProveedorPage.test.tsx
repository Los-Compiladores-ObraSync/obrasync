import { fireEvent, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { delay, http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it } from 'vitest'
import { reiniciarOrdenesCompraMock } from '../mocks/handlers'
import { server } from '../mocks/server'
import { MisOrdenesProveedorPage } from './MisOrdenesProveedorPage'

const RUTA_LISTA = '*/api/ordenes-compra'
const RUTA_DESPACHAR = '*/api/ordenes-compra/:id/despachar'
const CODIGO_1 = 'C1C2C3C4' // las tres órdenes del mock empiezan igual: se distinguen por el final del id

function renderPagina() {
  return render(
    <MemoryRouter>
      <MisOrdenesProveedorPage />
    </MemoryRouter>,
  )
}

/** Las tarjetas de orden, en el orden en que se ven. La lista está desde el inicio: se espera a que lleguen las órdenes. */
async function tarjetas() {
  return screen.findAllByRole('listitem')
}

function problema(status: number, detail: string) {
  return HttpResponse.json({ title: 'Error', status, detail }, { status })
}

const base = {
  licitacionId: 'l1',
  cotizacionId: 'c1',
  obraId: 'o1',
  proveedorId: 'p1',
}

describe('MisOrdenesProveedorPage', () => {
  beforeEach(() => reiniciarOrdenesCompraMock())

  it('lista las órdenes y distingue las despachadas de las pendientes', async () => {
    server.use(
      http.get(RUTA_LISTA, () =>
        HttpResponse.json([
          { ...base, id: 'aaaaaaaa-0000', estado: 'Emitida', fechaEmision: '2026-11-18T12:00:00Z', fueDespachada: false },
          {
            ...base,
            id: 'bbbbbbbb-0000',
            estado: 'Despachada',
            fechaEmision: '2026-11-12T12:00:00Z',
            fueDespachada: true,
            fechaDespacho: '2026-11-13T12:00:00Z',
          },
        ]),
      ),
    )

    renderPagina()

    expect(await screen.findByText('Emitida, falta despachar')).toBeInTheDocument()
    expect(screen.getByText('Despachada')).toBeInTheDocument()
  })

  it('avisa cuando todavía no hay órdenes', async () => {
    server.use(http.get(RUTA_LISTA, () => HttpResponse.json([])))

    renderPagina()

    expect(await screen.findByText('Todavía no tenés órdenes de compra.')).toBeInTheDocument()
  })

  it('solo las órdenes sin despachar tienen el botón "Lo despaché"; las despachadas muestran sus datos', async () => {
    renderPagina()

    const filas = await tarjetas()
    expect(within(filas[0]).getByRole('button', { name: /^Lo despaché/ })).toBeInTheDocument()
    expect(within(filas[1]).getByRole('button', { name: /^Lo despaché/ })).toBeInTheDocument()
    expect(within(filas[2]).queryByRole('button', { name: /^Lo despaché/ })).not.toBeInTheDocument()
    expect(within(filas[2]).getByText('13 de noviembre de 2026')).toBeInTheDocument()
    expect(within(filas[2]).getByText('20 de noviembre de 2026')).toBeInTheDocument()
    expect(within(filas[2]).getByText('Sale en camión por la mañana')).toBeInTheDocument()
  })

  it('despacha con fecha estimada y observación, y lo avisa', async () => {
    let cuerpo: unknown = null
    server.use(
      http.post(RUTA_DESPACHAR, async ({ request, params }) => {
        cuerpo = await request.json()
        return HttpResponse.json({
          ...base,
          id: params.id,
          estado: 'Despachada',
          fechaEmision: '2026-11-18T12:00:00Z',
          fueDespachada: true,
          fechaDespacho: '2026-11-21T12:00:00Z',
          fechaEstimadaLlegada: '2099-01-10T12:00:00Z',
          observacion: 'Llega por la tarde',
        })
      }),
    )
    const user = userEvent.setup()
    renderPagina()

    const filas = await tarjetas()
    await user.click(within(filas[0]).getByRole('button', { name: /^Lo despaché/ }))
    fireEvent.change(screen.getByLabelText('Fecha estimada de llegada (opcional)'), {
      target: { value: '2099-01-10' },
    })
    await user.type(screen.getByLabelText('Observación (opcional)'), 'Llega por la tarde')
    await user.click(screen.getByRole('button', { name: 'Confirmar despacho' }))

    expect(await screen.findByRole('status')).toHaveTextContent(`Avisaste que despachaste la orden ${CODIGO_1}`)
    expect(cuerpo).toEqual({ fechaEstimadaLlegada: '2099-01-10T12:00:00Z', observacion: 'Llega por la tarde' })
    // La orden pasa al final como despachada y ya no ofrece el botón ni el formulario.
    expect(screen.queryByRole('form')).not.toBeInTheDocument()
    expect(screen.getAllByText('Despachada')).toHaveLength(2)
    expect(screen.getAllByRole('button', { name: /^Lo despaché/ })).toHaveLength(1)
  })

  it('despacha sin fecha ni observación: no manda cuerpo', async () => {
    let cuerpo: string | null = null
    server.use(
      http.post(RUTA_DESPACHAR, async ({ request, params }) => {
        cuerpo = await request.text()
        return HttpResponse.json({
          ...base,
          id: params.id,
          estado: 'Despachada',
          fechaEmision: '2026-11-18T12:00:00Z',
          fueDespachada: true,
          fechaDespacho: '2026-11-21T12:00:00Z',
        })
      }),
    )
    const user = userEvent.setup()
    renderPagina()

    const filas = await tarjetas()
    await user.click(within(filas[0]).getByRole('button', { name: /^Lo despaché/ }))
    await user.click(screen.getByRole('button', { name: 'Confirmar despacho' }))

    expect(await screen.findByRole('status')).toHaveTextContent('Avisaste que despachaste la orden')
    expect(cuerpo).toBe('')
  })

  it('rechaza una fecha estimada pasada sin llamar al servidor', async () => {
    let pedidos = 0
    server.use(
      http.post(RUTA_DESPACHAR, () => {
        pedidos += 1
        return HttpResponse.json({})
      }),
    )
    const user = userEvent.setup()
    renderPagina()

    const filas = await tarjetas()
    await user.click(within(filas[0]).getByRole('button', { name: /^Lo despaché/ }))
    fireEvent.change(screen.getByLabelText('Fecha estimada de llegada (opcional)'), {
      target: { value: '2020-01-01' },
    })
    await user.click(screen.getByRole('button', { name: 'Confirmar despacho' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'La fecha estimada de llegada no puede ser anterior a hoy.',
    )
    expect(pedidos).toBe(0)
  })

  it('la observación tiene un tope de 200 caracteres y muestra el contador', async () => {
    const user = userEvent.setup()
    renderPagina()

    const filas = await tarjetas()
    await user.click(within(filas[0]).getByRole('button', { name: /^Lo despaché/ }))
    const campo = screen.getByLabelText('Observación (opcional)')

    expect(campo).toHaveAttribute('maxlength', '200')
    await user.type(campo, 'Hola')
    expect(screen.getByText('4/200')).toBeInTheDocument()
  })

  it('"Cancelar" cierra el formulario sin despachar nada', async () => {
    let pedidos = 0
    server.use(
      http.post(RUTA_DESPACHAR, () => {
        pedidos += 1
        return HttpResponse.json({})
      }),
    )
    const user = userEvent.setup()
    renderPagina()

    const filas = await tarjetas()
    await user.click(within(filas[0]).getByRole('button', { name: /^Lo despaché/ }))
    await user.click(screen.getByRole('button', { name: 'Cancelar' }))

    expect(screen.queryByRole('form')).not.toBeInTheDocument()
    expect(within(filas[0]).getByRole('button', { name: /^Lo despaché/ })).toBeInTheDocument()
    expect(pedidos).toBe(0)
  })

  it('un doble toque en "Confirmar despacho" manda un solo pedido', async () => {
    let pedidos = 0
    server.use(
      http.post(RUTA_DESPACHAR, async ({ params }) => {
        pedidos += 1
        await delay(50)
        return HttpResponse.json({
          ...base,
          id: params.id,
          estado: 'Despachada',
          fechaEmision: '2026-11-18T12:00:00Z',
          fueDespachada: true,
          fechaDespacho: '2026-11-21T12:00:00Z',
        })
      }),
    )
    const user = userEvent.setup()
    renderPagina()

    const filas = await tarjetas()
    await user.click(within(filas[0]).getByRole('button', { name: /^Lo despaché/ }))
    await user.dblClick(screen.getByRole('button', { name: 'Confirmar despacho' }))

    await screen.findByText(/Avisaste que despachaste/)
    expect(pedidos).toBe(1)
  })

  it('403: avisa que esa orden no es suya y deja el formulario abierto', async () => {
    server.use(http.post(RUTA_DESPACHAR, () => problema(403, 'Acceso denegado.')))
    const user = userEvent.setup()
    renderPagina()

    const filas = await tarjetas()
    await user.click(within(filas[0]).getByRole('button', { name: /^Lo despaché/ }))
    await user.click(screen.getByRole('button', { name: 'Confirmar despacho' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('No podés despachar esta orden')
    expect(screen.getByRole('button', { name: 'Confirmar despacho' })).toBeEnabled()
  })

  it('400: muestra el detalle que informa el servicio', async () => {
    server.use(http.post(RUTA_DESPACHAR, () => problema(400, 'La fecha estimada de llegada no puede ser anterior a hoy.')))
    const user = userEvent.setup()
    renderPagina()

    const filas = await tarjetas()
    await user.click(within(filas[0]).getByRole('button', { name: /^Lo despaché/ }))
    await user.click(screen.getByRole('button', { name: 'Confirmar despacho' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'La fecha estimada de llegada no puede ser anterior a hoy.',
    )
  })

  it('404: avisa que la orden ya no existe y actualiza la lista', async () => {
    const orden = { ...base, id: 'c1c2c3c4-0000', estado: 'Emitida', fechaEmision: '2026-11-18T12:00:00Z', fueDespachada: false }
    // Primera carga: la orden está. Cuando se vuelve a pedir la lista ya no existe.
    server.use(
      http.get(RUTA_LISTA, () => HttpResponse.json([orden], { headers: { 'X-Total-Count': '1' } }), { once: true }),
      http.get(RUTA_LISTA, () => HttpResponse.json([])),
      http.post(RUTA_DESPACHAR, () => problema(404, 'No se encontró la orden de compra.')),
    )
    const user = userEvent.setup()
    renderPagina()

    const filas = await tarjetas()
    await user.click(within(filas[0]).getByRole('button', { name: /^Lo despaché/ }))
    await user.click(screen.getByRole('button', { name: 'Confirmar despacho' }))

    expect(await screen.findByText('Todavía no tenés órdenes de compra.')).toBeInTheDocument()
  })

  it('si el servidor no responde, avisa y deja intentar de nuevo', async () => {
    server.use(http.post(RUTA_DESPACHAR, () => HttpResponse.error()))
    const user = userEvent.setup()
    renderPagina()

    const filas = await tarjetas()
    await user.click(within(filas[0]).getByRole('button', { name: /^Lo despaché/ }))
    await user.click(screen.getByRole('button', { name: 'Confirmar despacho' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'No se pudo conectar con el servidor. Intentá nuevamente.',
    )
    expect(screen.getByRole('button', { name: 'Confirmar despacho' })).toBeEnabled()
  })
})
