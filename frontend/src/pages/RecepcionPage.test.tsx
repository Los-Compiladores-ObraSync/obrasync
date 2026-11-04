import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it } from 'vitest'
import { reiniciarRecepcionesMock } from '../mocks/handlers'
import { server } from '../mocks/server'
import { hoyComoTexto } from '../obras/fechaEnPalabras'
import { RecepcionPage } from './RecepcionPage'

const RUTA_ORDENES = '*/api/recepciones/ordenes-pendientes'
const RUTA_RECEPCIONES = '*/api/recepciones'

function renderPagina() {
  return render(
    <MemoryRouter>
      <RecepcionPage />
    </MemoryRouter>,
  )
}

/** Espera la lista y toca la primera orden (la de 80 bolsas de cemento y 12 m³ de arena). */
async function elegirPrimeraOrden(user: ReturnType<typeof userEvent.setup>) {
  const tarjetas = await screen.findAllByRole('button', { name: /Torre Alvear/ })
  await user.click(tarjetas[0])
}

describe('RecepcionPage', () => {
  beforeEach(() => reiniciarRecepcionesMock())

  it('lista las órdenes pendientes con la obra y sus materiales', async () => {
    renderPagina()

    expect(await screen.findAllByRole('button', { name: /Torre Alvear/ })).toHaveLength(2)
    expect(screen.getByText('80 bolsas · Cemento Portland Especial 50kg')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: '← Volver a Obra' })).toHaveAttribute('href', '/obra')
  })

  it('al elegir una orden muestra lo que tiene que haber llegado y el formulario del remito', async () => {
    const user = userEvent.setup()
    renderPagina()

    await elegirPrimeraOrden(user)

    expect(screen.getByText('Obra: Torre Alvear')).toBeInTheDocument()
    expect(screen.getByText('80 bolsas · Cemento Portland Especial 50kg')).toBeInTheDocument()
    expect(screen.getByLabelText('Número de remito')).toBeInTheDocument()
    expect(screen.getByLabelText('Fecha de recepción')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Llegó todo: confirmar recepción' })).toBeEnabled()
  })

  it('la fecha arranca en hoy y no deja elegir fechas futuras', async () => {
    const user = userEvent.setup()
    renderPagina()

    await elegirPrimeraOrden(user)

    const fecha = screen.getByLabelText('Fecha de recepción')
    expect(fecha).toHaveValue(hoyComoTexto())
    expect(fecha).toHaveAttribute('max', hoyComoTexto())
  })

  it('"Elegir otra orden" vuelve a la lista', async () => {
    const user = userEvent.setup()
    renderPagina()

    await elegirPrimeraOrden(user)
    await user.click(screen.getByRole('button', { name: '← Elegir otra orden' }))

    expect(await screen.findAllByRole('button', { name: /Torre Alvear/ })).toHaveLength(2)
  })

  it('pide el número de remito antes de enviar', async () => {
    const user = userEvent.setup()
    renderPagina()

    await elegirPrimeraOrden(user)
    await user.click(screen.getByRole('button', { name: 'Llegó todo: confirmar recepción' }))

    expect(screen.getByText('El número de remito es obligatorio.')).toBeInTheDocument()
  })

  it('no acepta una fecha futura (#127)', async () => {
    const user = userEvent.setup()
    renderPagina()

    await elegirPrimeraOrden(user)
    await user.type(screen.getByLabelText('Número de remito'), '0001-00012345')
    fireEvent.change(screen.getByLabelText('Fecha de recepción'), { target: { value: '2999-12-31' } })
    await user.click(screen.getByRole('button', { name: 'Llegó todo: confirmar recepción' }))

    expect(screen.getByText('La fecha de recepción no puede ser futura.')).toBeInTheDocument()
  })

  it('registra la recepción, la confirma y la orden sale de la lista', async () => {
    const user = userEvent.setup()
    renderPagina()

    await elegirPrimeraOrden(user)
    await user.type(screen.getByLabelText('Número de remito'), '0001-00012345')
    await user.click(screen.getByRole('button', { name: 'Llegó todo: confirmar recepción' }))

    expect(await screen.findByRole('heading', { level: 1, name: 'Recepción registrada' })).toBeInTheDocument()
    expect(screen.getByText('0001-00012345')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Recibir otra orden' }))

    // Queda una sola: la que se recibió ya no está pendiente.
    expect(await screen.findAllByRole('button', { name: /Torre Alvear/ })).toHaveLength(1)
  })

  it('manda al backend las cantidades compradas como recibidas (recepción total)', async () => {
    let cuerpo: unknown
    server.use(
      http.post(RUTA_RECEPCIONES, async ({ request }) => {
        cuerpo = await request.json()
        return HttpResponse.json(
          { id: 'x', obraId: 'o', ordenCompraId: 'oc', numeroRemito: 'R-1', fechaRecepcion: '2026-11-03T00:00:00.000Z', estado: 3 },
          { status: 201 },
        )
      }),
    )
    const user = userEvent.setup()
    renderPagina()

    await elegirPrimeraOrden(user)
    await user.type(screen.getByLabelText('Número de remito'), 'R-1')
    await user.click(screen.getByRole('button', { name: 'Llegó todo: confirmar recepción' }))
    await screen.findByRole('heading', { level: 1, name: 'Recepción registrada' })

    const detalles = (cuerpo as { detalles: { cantidadEsperada: number; cantidadRecibida: number; conforme: boolean }[] }).detalles
    expect(detalles).toHaveLength(2)
    expect(detalles.every((d) => d.cantidadRecibida === d.cantidadEsperada && d.conforme)).toBe(true)
  })

  it('muestra el motivo si el servidor dice que el remito ya se usó (409)', async () => {
    server.use(
      http.post(RUTA_RECEPCIONES, () =>
        HttpResponse.json(
          { title: 'Conflicto', status: 409, detail: 'El número de remito ya se usó en esta orden.' },
          { status: 409 },
        ),
      ),
    )
    const user = userEvent.setup()
    renderPagina()

    await elegirPrimeraOrden(user)
    await user.type(screen.getByLabelText('Número de remito'), '0001-00012345')
    await user.click(screen.getByRole('button', { name: 'Llegó todo: confirmar recepción' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('El número de remito ya se usó en esta orden.')
    // El formulario sigue ahí para corregir el remito.
    expect(screen.getByLabelText('Número de remito')).toHaveValue('0001-00012345')
  })

  it('avisa si no hay órdenes para recibir', async () => {
    server.use(http.get(RUTA_ORDENES, () => HttpResponse.json([])))
    renderPagina()

    expect(await screen.findByText('No hay órdenes de compra para recibir.')).toBeInTheDocument()
  })

  it('avisa y deja reintentar si falla la carga de las órdenes', async () => {
    server.use(http.get(RUTA_ORDENES, () => HttpResponse.error()))
    const user = userEvent.setup()
    renderPagina()

    expect(
      await screen.findByText('No se pudieron cargar las órdenes de compra. Intentá nuevamente.'),
    ).toBeInTheDocument()

    server.resetHandlers()
    await user.click(screen.getByRole('button', { name: 'Reintentar' }))

    expect(await screen.findAllByRole('button', { name: /Torre Alvear/ })).toHaveLength(2)
  })
})
