import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { reiniciarRecepcionesMock } from '../mocks/handlers'
import { server } from '../mocks/server'
import { hoyComoTexto } from '../obras/fechaEnPalabras'
import { ErrorDeImagen, reducirImagen } from '../recepciones/imagenRemito'
import { leerRemitoDeFoto, obtenerHistorialRecepciones, obtenerOrdenesPendientes, registrarRecepcion } from '../recepciones/recepcionesService'
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
    expect(screen.getByText(/Pedido 80 bolsas.*Recibido 0 bolsas.*Pendiente 80 bolsas/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: '← Volver al inicio' })).toHaveAttribute('href', '/')
  })

  it('al elegir una orden muestra lo que tiene que haber llegado y el formulario del remito', async () => {
    const user = userEvent.setup()
    renderPagina()

    await elegirPrimeraOrden(user)

    expect(screen.getByText('Obra: Torre Alvear')).toBeInTheDocument()
    expect(screen.getByLabelText(/Cemento Portland Especial 50kg.*recibido en esta entrega/i)).toHaveValue(80)
    expect(screen.getByLabelText('Número de remito')).toBeInTheDocument()
    expect(screen.getByLabelText('Fecha de recepción')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Confirmar recepción' })).toBeEnabled()
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
    await user.click(screen.getByRole('button', { name: 'Confirmar recepción' }))

    expect(screen.getByText('El número de remito es obligatorio.')).toBeInTheDocument()
  })

  it('no acepta una fecha futura (#127)', async () => {
    const user = userEvent.setup()
    renderPagina()

    await elegirPrimeraOrden(user)
    await user.type(screen.getByLabelText('Número de remito'), '0001-00012345')
    fireEvent.change(screen.getByLabelText('Fecha de recepción'), { target: { value: '2999-12-31' } })
    await user.click(screen.getByRole('button', { name: 'Confirmar recepción' }))

    expect(screen.getByText('La fecha de recepción no puede ser futura.')).toBeInTheDocument()
  })

  it('registra la recepción, la confirma y la orden sale de la lista', async () => {
    const user = userEvent.setup()
    renderPagina()

    await elegirPrimeraOrden(user)
    await user.type(screen.getByLabelText('Número de remito'), '0001-00012345')
    await user.click(screen.getByRole('button', { name: 'Confirmar recepción' }))

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
    await user.click(screen.getByRole('button', { name: 'Confirmar recepción' }))
    await screen.findByRole('heading', { level: 1, name: 'Recepción registrada' })

    const detalles = (cuerpo as { detalles: { cantidadEsperada: number; cantidadRecibida: number; conforme: boolean }[] }).detalles
    expect(detalles).toHaveLength(2)
    expect(detalles.every((d) => d.cantidadRecibida === d.cantidadEsperada && d.conforme)).toBe(true)
  })

  it('el mock rechaza omitir artículos y deja intacto el saldo de la orden', async () => {
    const orden = (await obtenerOrdenesPendientes())[0]
    await expect(registrarRecepcion({
      obraId: orden.obraId,
      ordenCompraId: orden.ordenCompraId,
      numeroRemito: 'R-INCOMPLETO',
      fechaRecepcion: '2026-11-19T00:00:00.000Z',
      detalles: orden.items.slice(0, 1).map((item) => ({
        articuloId: item.articuloId,
        descripcionArticulo: item.descripcionArticulo,
        cantidadEsperada: item.cantidadEsperada,
        cantidadRecibida: 1,
        unidadMedida: item.unidadMedida,
        conforme: true,
      })),
    })).rejects.toMatchObject({ status: 400 })
    const actualizada = (await obtenerOrdenesPendientes())[0]
    expect(actualizada.items.map((item) => item.cantidadRecibida)).toEqual([0, 0])
  })

  it('registra una entrega parcial con ceros para los materiales que no llegaron y conserva la orden pendiente', async () => {
    reiniciarRecepcionesMock({ cantidadCementoInicial: 100 })
    const user = userEvent.setup()
    renderPagina()
    await elegirPrimeraOrden(user)
    await user.clear(screen.getByLabelText(/Cemento Portland Especial 50kg.*recibido en esta entrega/i))
    await user.type(screen.getByLabelText(/Cemento Portland Especial 50kg.*recibido en esta entrega/i), '75')
    await user.clear(screen.getByLabelText(/Arena Fina a Granel.*recibido en esta entrega/i))
    await user.type(screen.getByLabelText(/Arena Fina a Granel.*recibido en esta entrega/i), '12')
    await user.type(screen.getByLabelText('Número de remito'), 'R-P')
    await user.click(screen.getByRole('button', { name: 'Confirmar recepción' }))
    expect(await screen.findByText(/todavía queda mercadería pendiente/i)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Recibir otra orden' }))
    const ordenParcial = await screen.findByRole('button', { name: /Cemento Portland Especial 50kg.*Pendiente 25 bolsas/ })
    await user.click(ordenParcial)
    expect(screen.getByLabelText(/Cemento Portland Especial 50kg.*recibido en esta entrega/i)).toHaveValue(25)
    expect(screen.getByLabelText(/Arena Fina a Granel.*recibido en esta entrega/i)).toHaveValue(0)
    expect(await screen.findByText('Remito R-P')).toBeInTheDocument()
    expect(screen.getByLabelText('Historial de recepciones').textContent).toContain('Parcial')
    expect(screen.getByLabelText('Historial de recepciones').textContent).toContain('jueves, 19 de noviembre de 2026')
    expect(screen.getByText(/Cemento Portland Especial 50kg: 75 bolsas/)).toBeInTheDocument()
  })

  it('una segunda entrega completa la orden y deja ambas entregas en el historial MSW', async () => {
    reiniciarRecepcionesMock({ cantidadCementoInicial: 100 })
    const ordenInicial = (await obtenerOrdenesPendientes())[0]
    const user = userEvent.setup()
    renderPagina()
    await elegirPrimeraOrden(user)
    await user.clear(screen.getByLabelText(/Cemento Portland Especial 50kg.*recibido en esta entrega/i))
    await user.type(screen.getByLabelText(/Cemento Portland Especial 50kg.*recibido en esta entrega/i), '75')
    await user.clear(screen.getByLabelText(/Arena Fina a Granel.*recibido en esta entrega/i))
    await user.type(screen.getByLabelText(/Arena Fina a Granel.*recibido en esta entrega/i), '12')
    await user.type(screen.getByLabelText('Número de remito'), 'R-001')
    await user.click(screen.getByRole('button', { name: 'Confirmar recepción' }))
    expect(await screen.findByText(/Recepción parcial: todavía queda/i)).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Recibir otra orden' }))
    await user.click(await screen.findByRole('button', { name: /Cemento Portland Especial 50kg.*Pendiente 25 bolsas/ }))
    expect(await screen.findByText('Remito R-001')).toBeInTheDocument()
    expect(screen.getByLabelText('Historial de recepciones').textContent).toContain('Parcial')
    await user.type(screen.getByLabelText('Número de remito'), 'R-002')
    await user.click(screen.getByRole('button', { name: 'Confirmar recepción' }))
    expect(await screen.findByText('La orden quedó recibida por completo.')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Recibir otra orden' }))
    expect(await screen.findAllByRole('button', { name: /Torre Alvear/ })).toHaveLength(1)

    const historial = await obtenerHistorialRecepciones('e1e2e3e4-0000-0000-0000-000000000001')
    expect(historial.map((entrega) => [entrega.numeroRemito, entrega.estado])).toEqual([['R-001', 2], ['R-002', 3]])
    expect(historial[0].detalles?.map((detalle) => detalle.cantidadRecibida)).toEqual([75, 12])
    expect(historial[1].detalles?.map((detalle) => detalle.cantidadRecibida)).toEqual([25, 0])
    await expect(registrarRecepcion({
      obraId: ordenInicial.obraId,
      ordenCompraId: ordenInicial.ordenCompraId,
      numeroRemito: 'R-DESP-OC',
      fechaRecepcion: '2026-11-19T00:00:00.000Z',
      detalles: ordenInicial.items.map((item) => ({
        articuloId: item.articuloId,
        descripcionArticulo: item.descripcionArticulo,
        cantidadEsperada: item.cantidadEsperada,
        cantidadRecibida: item.cantidadEsperada,
        unidadMedida: item.unidadMedida,
        conforme: true,
      })),
    })).rejects.toMatchObject({ status: 409 })
  })

  it('bloquea cantidades superiores al saldo y el envío con todos los artículos en cero', async () => {
    let llamadas = 0
    server.use(http.post(RUTA_RECEPCIONES, () => { llamadas += 1; return HttpResponse.json({}, { status: 201 }) }))
    const user = userEvent.setup()
    renderPagina()
    await elegirPrimeraOrden(user)
    const cemento = screen.getByLabelText(/Cemento Portland Especial 50kg.*recibido en esta entrega/i)
    await user.clear(cemento)
    await user.type(cemento, '81')
    await user.type(screen.getByLabelText('Número de remito'), 'R-X')
    await user.click(screen.getByRole('button', { name: 'Confirmar recepción' }))
    expect(screen.getByText(/No puede superar el saldo de 80/)).toBeInTheDocument()
    expect(llamadas).toBe(0)
    await user.clear(cemento)
    await user.type(cemento, '0')
    await user.clear(screen.getByLabelText(/Arena Fina a Granel.*recibido en esta entrega/i))
    await user.type(screen.getByLabelText(/Arena Fina a Granel.*recibido en esta entrega/i), '0')
    await user.click(screen.getByRole('button', { name: 'Confirmar recepción' }))
    expect(screen.getByText(/al menos un artículo/i)).toBeInTheDocument()
    expect(llamadas).toBe(0)
  })

  it('rechaza una cantidad negativa antes de llamar al POST', async () => {
    let llamadas = 0
    server.use(http.post(RUTA_RECEPCIONES, () => { llamadas += 1; return HttpResponse.json({}, { status: 201 }) }))
    const user = userEvent.setup()
    renderPagina()
    await elegirPrimeraOrden(user)
    const campo = screen.getByLabelText(/Cemento Portland Especial 50kg.*recibido en esta entrega/i)
    fireEvent.change(campo, { target: { value: '-1' } })
    await user.type(screen.getByLabelText('Número de remito'), 'R-NEG')
    await user.click(screen.getByRole('button', { name: 'Confirmar recepción' }))
    expect(screen.getByText('La cantidad no puede ser negativa.')).toBeInTheDocument()
    expect(llamadas).toBe(0)
  })

  it('muestra errores 400, conserva los campos y permite volver a enviar', async () => {
    let llamadas = 0
    server.use(http.post(RUTA_RECEPCIONES, async ({ request }) => {
      llamadas += 1
      if (llamadas === 1) return HttpResponse.json({ title: 'Datos inválidos', status: 400, detail: 'El remito requiere corrección.' }, { status: 400 })
      const cuerpo = await request.json() as { numeroRemito: string }
      return HttpResponse.json({ id: 'id', numeroRemito: cuerpo.numeroRemito, estado: 3 }, { status: 201 })
    }))
    const user = userEvent.setup()
    renderPagina()
    await elegirPrimeraOrden(user)
    await user.type(screen.getByLabelText('Número de remito'), 'R-CORREGIR')
    await user.click(screen.getByRole('button', { name: 'Confirmar recepción' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('El remito requiere corrección.')
    expect(screen.getByLabelText('Número de remito')).toHaveValue('R-CORREGIR')
    await user.clear(screen.getByLabelText('Número de remito'))
    await user.type(screen.getByLabelText('Número de remito'), 'R-OK')
    await user.click(screen.getByRole('button', { name: 'Confirmar recepción' }))
    expect(await screen.findByText('La orden quedó recibida por completo.')).toBeInTheDocument()
    expect(llamadas).toBe(2)
  })

  it('muestra carga, estado vacío y error del historial', async () => {
    let liberarCarga: (() => void) | undefined
    let indicarSolicitud: (() => void) | undefined
    const solicitud = new Promise<void>((resolve) => { indicarSolicitud = resolve })
    server.use(http.get(RUTA_RECEPCIONES, async () => {
      indicarSolicitud?.()
      await new Promise<void>((resolve) => { liberarCarga = resolve })
      return HttpResponse.json([])
    }))
    const user = userEvent.setup()
    renderPagina()
    await elegirPrimeraOrden(user)
    await solicitud
    expect(screen.getByText('Cargando historial…')).toBeInTheDocument()
    liberarCarga?.()
    expect(await screen.findByText('Todavía no hay entregas registradas.')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: '← Elegir otra orden' }))
    server.use(http.get(RUTA_RECEPCIONES, () => HttpResponse.error()))
    await elegirPrimeraOrden(user)
    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudo cargar el historial.')
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
    await user.click(screen.getByRole('button', { name: 'Confirmar recepción' }))

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

    it('manda la imagen de lectura con Content-Type multipart', async () => {
      let contentType: string | null = null
      server.use(
        http.post(RUTA_LEER_REMITO, ({ request }) => {
          contentType = request.headers.get('content-type')
          return HttpResponse.json({ numeroRemito: 'R-1', fecha: null })
        }),
      )
      const user = userEvent.setup()
      renderPagina()
      await elegirPrimeraOrden(user)

      await sacarFoto(user)

      await screen.findByText(/Leí el número del remito/)
      expect(reducirImagen).toHaveBeenCalledTimes(1)
      expect(contentType).toMatch(/^multipart\/form-data; boundary=/)
    })

    it('el servicio adjunta el archivo en el campo multipart "imagen"', async () => {
      let cuerpo: BodyInit | null | undefined
      let contentType: string | null = null
      vi.stubGlobal('fetch', vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
        cuerpo = init?.body
        contentType = new Headers(init?.headers).get('content-type')
        return new Response(JSON.stringify({ numeroRemito: 'R-1', fecha: null }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        })
      }))
      try {
        await leerRemitoDeFoto(new Blob(['foto'], { type: 'image/jpeg' }))
        expect(cuerpo).toBeInstanceOf(FormData)
        const imagen = (cuerpo as FormData).get('imagen')
        expect(imagen).toBeInstanceOf(Blob)
        expect((imagen as File).name).toBe('remito.jpg')
        expect(contentType).toBeNull()
      } finally {
        vi.unstubAllGlobals()
      }
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
      let liberarRespuesta: (() => void) | undefined
      let indicarSolicitudIniciada: (() => void) | undefined
      const solicitudIniciada = new Promise<void>((resolve) => { indicarSolicitudIniciada = resolve })
      server.use(
        http.post(RUTA_LEER_REMITO, async () => {
          indicarSolicitudIniciada?.()
          await new Promise<void>((resolve) => { liberarRespuesta = resolve })
          return HttpResponse.json({ numeroRemito: 'R-5', fecha: null })
        }),
      )
      const user = userEvent.setup()
      renderPagina()
      await elegirPrimeraOrden(user)

      const carga = user.upload(screen.getByLabelText('Foto del remito'), foto())
      await solicitudIniciada
      expect(await screen.findByRole('button', { name: 'Leyendo el remito…' })).toBeDisabled()
      expect(screen.getByRole('button', { name: 'Confirmar recepción' })).toBeDisabled()
      liberarRespuesta?.()
      await carga
      await waitFor(() => expect(screen.getByRole('button', { name: 'Sacar foto del remito' })).toBeEnabled())
      expect(screen.getByRole('button', { name: 'Confirmar recepción' })).toBeEnabled()
    })

    it('si se cambia de orden mientras lee, la lectura vieja no pisa el formulario nuevo', async () => {
      let liberarRespuesta: (() => void) | undefined
      let indicarSolicitudIniciada: (() => void) | undefined
      const solicitudIniciada = new Promise<void>((resolve) => { indicarSolicitudIniciada = resolve })
      server.use(
        http.post(RUTA_LEER_REMITO, async () => {
          indicarSolicitudIniciada?.()
          await new Promise<void>((resolve) => { liberarRespuesta = resolve })
          return HttpResponse.json({ numeroRemito: 'R-VIEJO', fecha: null })
        }),
      )
      const user = userEvent.setup()
      renderPagina()
      await elegirPrimeraOrden(user)
      const carga = user.upload(screen.getByLabelText('Foto del remito'), foto())
      await solicitudIniciada
      await screen.findByRole('button', { name: 'Leyendo el remito…' })

      await user.click(screen.getByRole('button', { name: '← Elegir otra orden' }))
      await elegirPrimeraOrden(user)
      liberarRespuesta?.()
      await carga

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

      await user.click(screen.getByRole('button', { name: 'Confirmar recepción' }))

      await screen.findByRole('heading', { level: 1, name: 'Recepción registrada' })
      expect(cuerpo?.numeroRemito).toBe('R-0001-00012345')
      expect(cuerpo?.fechaRecepcion).toBe(`${ayerComoTexto()}T00:00:00.000Z`)
    })
  })
})





