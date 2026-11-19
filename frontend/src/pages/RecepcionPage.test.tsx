import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { delay, http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { reiniciarRecepcionesMock } from '../mocks/handlers'
import { server } from '../mocks/server'
import { hoyComoTexto } from '../obras/fechaEnPalabras'
import { ErrorDeImagen, reducirImagen } from '../recepciones/imagenRemito'
import { RecepcionPage } from './RecepcionPage'

// La foto se achica con APIs del navegador que jsdom no tiene: en estas pruebas se simula.
vi.mock('../recepciones/imagenRemito', async (original) => ({
  ...(await original<typeof import('../recepciones/imagenRemito')>()),
  reducirImagen: vi.fn(),
}))

const RUTA_ORDENES = '*/api/recepciones/ordenes-pendientes'
const RUTA_RECEPCIONES = '*/api/recepciones'
const RUTA_LEER_REMITO = '*/api/asistente/remito/leer'

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
    expect(screen.getByRole('link', { name: '← Volver al inicio' })).toHaveAttribute('href', '/')
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

  it('aclara que solo se muestran las órdenes que Compras ya despachó (#205)', async () => {
    renderPagina()

    expect(await screen.findAllByRole('button', { name: /Torre Alvear/ })).toHaveLength(2)
    expect(screen.getByText(/solo aparecen las órdenes que Compras ya despachó/)).toBeInTheDocument()
  })

  it('en el estado vacío dice que no hay órdenes despachadas pendientes de recibir (#205)', async () => {
    server.use(http.get(RUTA_ORDENES, () => HttpResponse.json([])))
    renderPagina()

    expect(await screen.findByText('Cuando Compras despache una orden, la vas a ver acá.')).toBeInTheDocument()
  })

  it('avisa si no hay órdenes para recibir', async () => {
    server.use(http.get(RUTA_ORDENES, () => HttpResponse.json([])))
    renderPagina()

    expect(await screen.findByText('No hay órdenes despachadas pendientes de recibir.')).toBeInTheDocument()
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

  describe('foto del remito (#209)', () => {
    const foto = () => new File(['pixeles'], 'remito.jpg', { type: 'image/jpeg' })

    function ayerComoTexto() {
      const ayer = new Date()
      ayer.setDate(ayer.getDate() - 1)
      return hoyComoTexto(ayer)
    }

    async function sacarFoto(user: ReturnType<typeof userEvent.setup>) {
      await user.upload(screen.getByLabelText('Foto del remito'), foto())
    }

    beforeEach(() => {
      vi.mocked(reducirImagen).mockReset()
      vi.mocked(reducirImagen).mockResolvedValue(new Blob(['jpg'], { type: 'image/jpeg' }))
    })

    it('el botón para sacar la foto está arriba del formulario y la foto es opcional', async () => {
      const user = userEvent.setup()
      renderPagina()

      await elegirPrimeraOrden(user)

      const boton = screen.getByRole('button', { name: 'Sacar foto del remito' })
      expect(boton).toBeEnabled()
      expect(screen.getByText(/Opcional: leo el número y la fecha por vos/)).toBeInTheDocument()
      // Sin foto, el formulario manual sigue funcionando igual.
      expect(screen.getByLabelText('Número de remito')).toHaveValue('')
      expect(boton.compareDocumentPosition(screen.getByLabelText('Número de remito')) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    })

    it('carga el número y la fecha leídos como sugerencia editable y NO registra nada', async () => {
      let registros = 0
      server.use(
        http.post(RUTA_RECEPCIONES, () => {
          registros += 1
          return HttpResponse.json({}, { status: 201 })
        }),
      )
      const user = userEvent.setup()
      renderPagina()
      await elegirPrimeraOrden(user)

      await sacarFoto(user)

      expect(await screen.findByText(/Leí el número y la fecha del remito/)).toBeInTheDocument()
      expect(screen.getByLabelText('Número de remito')).toHaveValue('R-0001-00012345')
      expect(screen.getByLabelText('Fecha de recepción')).toHaveValue(ayerComoTexto())
      expect(registros).toBe(0)

      // Se puede corregir a mano.
      await user.clear(screen.getByLabelText('Número de remito'))
      await user.type(screen.getByLabelText('Número de remito'), 'R-0001-00099999')
      expect(screen.getByLabelText('Número de remito')).toHaveValue('R-0001-00099999')
    })

    it('manda la imagen achicada como multipart en el campo "imagen"', async () => {
      let recibido: FormDataEntryValue | null = null
      server.use(
        http.post(RUTA_LEER_REMITO, async ({ request }) => {
          recibido = (await request.formData()).get('imagen')
          return HttpResponse.json({ numeroRemito: 'R-1', fecha: null })
        }),
      )
      const user = userEvent.setup()
      renderPagina()
      await elegirPrimeraOrden(user)

      await sacarFoto(user)

      await screen.findByText(/Leí el número del remito/)
      expect(reducirImagen).toHaveBeenCalledTimes(1)
      expect(recibido).not.toBeNull()
      expect((recibido as unknown as Blob).size).toBeGreaterThan(0)
    })

    it('si solo se leyó el número, la fecha queda en hoy y lo avisa', async () => {
      server.use(http.post(RUTA_LEER_REMITO, () => HttpResponse.json({ numeroRemito: 'R-77', fecha: null })))
      const user = userEvent.setup()
      renderPagina()
      await elegirPrimeraOrden(user)

      await sacarFoto(user)

      expect(await screen.findByText('Leí el número del remito. La fecha no se pudo leer: revisala.')).toBeInTheDocument()
      expect(screen.getByLabelText('Número de remito')).toHaveValue('R-77')
      expect(screen.getByLabelText('Fecha de recepción')).toHaveValue(hoyComoTexto())
    })

    it('no carga una fecha futura aunque el servidor la mande (#127)', async () => {
      const manana = new Date()
      manana.setDate(manana.getDate() + 3)
      server.use(
        http.post(RUTA_LEER_REMITO, () => HttpResponse.json({ numeroRemito: 'R-77', fecha: hoyComoTexto(manana) })),
      )
      const user = userEvent.setup()
      renderPagina()
      await elegirPrimeraOrden(user)

      await sacarFoto(user)

      await screen.findByText(/Leí el número del remito/)
      expect(screen.getByLabelText('Fecha de recepción')).toHaveValue(hoyComoTexto())
    })

    it('si no se pudo leer nada lo dice y no pisa lo que la persona ya había escrito', async () => {
      server.use(http.post(RUTA_LEER_REMITO, () => HttpResponse.json({ numeroRemito: null, fecha: null })))
      const user = userEvent.setup()
      renderPagina()
      await elegirPrimeraOrden(user)
      await user.type(screen.getByLabelText('Número de remito'), 'A-1')

      await sacarFoto(user)

      expect(await screen.findByRole('alert')).toHaveTextContent('No pude leer el remito en la foto')
      expect(screen.getByLabelText('Número de remito')).toHaveValue('A-1')
    })

    it.each([
      [400, /La foto no sirvió/],
      [429, /Hay muchas lecturas en este momento/],
      [503, /La lectura de remitos no está disponible ahora/],
    ])('si el servidor responde %i muestra un mensaje claro y se puede cargar a mano', async (status, texto) => {
      server.use(http.post(RUTA_LEER_REMITO, () => HttpResponse.json({ title: 'Error', status }, { status })))
      const user = userEvent.setup()
      renderPagina()
      await elegirPrimeraOrden(user)

      await sacarFoto(user)

      expect(await screen.findByRole('alert')).toHaveTextContent(texto)
      await user.type(screen.getByLabelText('Número de remito'), 'A-1')
      expect(screen.getByLabelText('Número de remito')).toHaveValue('A-1')
    })

    it('si la foto no se puede abrir muestra el motivo sin llamar al servidor', async () => {
      let llamadas = 0
      server.use(
        http.post(RUTA_LEER_REMITO, () => {
          llamadas += 1
          return HttpResponse.json({})
        }),
      )
      vi.mocked(reducirImagen).mockRejectedValue(new ErrorDeImagen('No pude abrir esa foto. Probá con otra o cargá el remito a mano.'))
      const user = userEvent.setup()
      renderPagina()
      await elegirPrimeraOrden(user)

      await sacarFoto(user)

      expect(await screen.findByRole('alert')).toHaveTextContent('No pude abrir esa foto')
      expect(llamadas).toBe(0)
    })

    it('mientras lee, el botón avisa y no se puede confirmar la recepción', async () => {
      server.use(
        http.post(RUTA_LEER_REMITO, async () => {
          await delay(150)
          return HttpResponse.json({ numeroRemito: 'R-5', fecha: null })
        }),
      )
      const user = userEvent.setup()
      renderPagina()
      await elegirPrimeraOrden(user)

      await sacarFoto(user)

      expect(await screen.findByRole('button', { name: 'Leyendo el remito…' })).toBeDisabled()
      expect(screen.getByRole('button', { name: 'Llegó todo: confirmar recepción' })).toBeDisabled()
      await waitFor(() => expect(screen.getByRole('button', { name: 'Sacar foto del remito' })).toBeEnabled())
      expect(screen.getByRole('button', { name: 'Llegó todo: confirmar recepción' })).toBeEnabled()
    })

    it('si se cambia de orden mientras lee, la lectura vieja no pisa el formulario nuevo', async () => {
      server.use(
        http.post(RUTA_LEER_REMITO, async () => {
          await delay(150)
          return HttpResponse.json({ numeroRemito: 'R-VIEJO', fecha: null })
        }),
      )
      const user = userEvent.setup()
      renderPagina()
      await elegirPrimeraOrden(user)
      await sacarFoto(user)
      await screen.findByRole('button', { name: 'Leyendo el remito…' })

      await user.click(screen.getByRole('button', { name: '← Elegir otra orden' }))
      await elegirPrimeraOrden(user)
      await delay(300)

      expect(screen.getByLabelText('Número de remito')).toHaveValue('')
      expect(screen.queryByText(/Leí el número/)).not.toBeInTheDocument()
    })

    it('después de la foto, lo que se registra es lo que quedó en el formulario (con la fecha leída)', async () => {
      let cuerpo: { numeroRemito: string; fechaRecepcion: string } | undefined
      server.use(
        http.post(RUTA_RECEPCIONES, async ({ request }) => {
          cuerpo = (await request.json()) as typeof cuerpo
          return HttpResponse.json(
            { id: 'x', obraId: 'o', ordenCompraId: 'oc', numeroRemito: cuerpo!.numeroRemito, fechaRecepcion: cuerpo!.fechaRecepcion, estado: 3 },
            { status: 201 },
          )
        }),
      )
      const user = userEvent.setup()
      renderPagina()
      await elegirPrimeraOrden(user)
      await sacarFoto(user)
      await screen.findByText(/Leí el número y la fecha del remito/)

      await user.click(screen.getByRole('button', { name: 'Llegó todo: confirmar recepción' }))

      await screen.findByRole('heading', { level: 1, name: 'Recepción registrada' })
      expect(cuerpo?.numeroRemito).toBe('R-0001-00012345')
      expect(cuerpo?.fechaRecepcion).toBe(`${ayerComoTexto()}T00:00:00.000Z`)
    })
  })
})
