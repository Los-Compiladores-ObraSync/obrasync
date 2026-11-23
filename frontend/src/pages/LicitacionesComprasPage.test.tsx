import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { delay, http, HttpResponse } from 'msw'
import { MemoryRouter, Route, Routes } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { reiniciarLicitacionesMock } from '../mocks/handlers'
import { server } from '../mocks/server'
import { LicitacionesComprasPage } from './LicitacionesComprasPage'

const LIC_ABIERTA_ID = 'b1b2c3d4-1111-0000-0000-000000000001'

function renderPagina(initialEntry = '/compras/licitaciones') {
  return render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <Routes>
        <Route path="/compras/licitaciones" element={<LicitacionesComprasPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('LicitacionesComprasPage', () => {
  beforeEach(() => {
    reiniciarLicitacionesMock()
  })

  it('tiene enlace para volver al inicio', () => {
    renderPagina()
    const enlaceVolver = screen.getByRole('link', { name: '← Volver al inicio' })
    expect(enlaceVolver).toBeInTheDocument()
    expect(enlaceVolver).toHaveAttribute('href', '/')
  })

  it('muestra estado de carga y luego la lista de licitaciones', async () => {
    renderPagina()

    expect(screen.getByRole('status')).toHaveTextContent('Cargando licitaciones…')

    expect(await screen.findByText('REQ-2026-0011')).toBeInTheDocument()
    expect(screen.getByText('REQ-2026-0012')).toBeInTheDocument()
  })

  it('muestra estado vacío cuando no hay licitaciones disponibles', async () => {
    server.use(
      http.get('*/api/licitaciones', () => {
        return HttpResponse.json([], { status: 200 })
      }),
    )

    renderPagina()

    expect(
      await screen.findByText('No hay licitaciones disponibles.'),
    ).toBeInTheDocument()
  })

  it('muestra error cuando falla la carga de licitaciones y permite reintentar', async () => {
    const user = userEvent.setup()
    let falloUnaVez = false

    server.use(
      http.get('*/api/licitaciones', () => {
        if (!falloUnaVez) {
          falloUnaVez = true
          return HttpResponse.json(
            { title: 'Error del servidor', status: 500 },
            { status: 500 },
          )
        }
        return HttpResponse.json(
          [
            {
              licitacionId: LIC_ABIERTA_ID,
              numeroRequisicion: 'REQ-2026-0011',
              estado: 'Abierta',
              fechaCreacion: '2026-10-20T10:00:00Z',
              fechaLimiteCotizacion: '2026-11-20T18:00:00Z',
              items: [],
            },
          ],
          { status: 200 },
        )
      }),
    )

    renderPagina()

    expect(
      await screen.findByText('No se pudieron cargar las licitaciones. Intentá nuevamente.'),
    ).toBeInTheDocument()

    const botonReintentar = screen.getByRole('button', { name: 'Reintentar' })
    await user.click(botonReintentar)

    expect(await screen.findByText('REQ-2026-0011')).toBeInTheDocument()
  })

  it('al seleccionar una licitación muestra la matriz de cotizaciones con precio total, flete, plazo y oferta ganadora destacada', async () => {
    const user = userEvent.setup()
    renderPagina()

    await screen.findByText('REQ-2026-0011')

    const botonesVer = screen.getAllByRole('button', { name: 'Ver cotizaciones' })
    await user.click(botonesVer[0])

    // El número va en su propio <span> (para que no se parta en el guion), por eso se busca el encabezado entero.
    expect(
      await screen.findByRole('heading', { level: 2, name: 'Cotizaciones para REQ-2026-0011' }),
    ).toBeInTheDocument()

    // Proveedor con ID abreviado (primeros 8 caracteres: f7a8b9c0 y e1a2b3c4)
    expect(screen.getByText('f7a8b9c0')).toBeInTheDocument()
    expect(screen.getByText('e1a2b3c4')).toBeInTheDocument()

    // Precios totales calculados según cantidades de la licitación (80 de cemento y 15 de arena):
    // Cotización 1: 80 * 8000 + 15 * 14000 = 640.000 + 210.000 = $ 850.000
    // Cotización 2: 80 * 8500 + 15 * 16000 = 680.000 + 240.000 = $ 920.000
    expect(screen.getByText('$ 850.000')).toBeInTheDocument()
    expect(screen.getByText('$ 920.000')).toBeInTheDocument()

    // Flete
    expect(screen.getByText('$ 15.000')).toBeInTheDocument()
    expect(screen.getByText('$ 25.000')).toBeInTheDocument()

    // Plazo de entrega
    expect(screen.getByText('5 días')).toBeInTheDocument()
    expect(screen.getByText('10 días')).toBeInTheDocument()

    // Puntajes
    expect(screen.getByText('92,5 de 100')).toBeInTheDocument()
    expect(screen.getByText('78 de 100')).toBeInTheDocument()

    // Oferta destacada señalizada solo a la ganadora (esGanadora: true)
    expect(screen.getByText('★ Oferta destacada')).toBeInTheDocument()
  })

  it('muestra mensaje cuando la licitación seleccionada no tiene cotizaciones', async () => {
    server.use(
      http.get('*/api/cotizaciones', () => {
        return HttpResponse.json([], { status: 200 })
      }),
    )

    renderPagina(`/compras/licitaciones?licitacionId=${LIC_ABIERTA_ID}`)

    expect(
      await screen.findByText('Esta licitación no tiene cotizaciones cargadas.'),
    ).toBeInTheDocument()
  })

  it('muestra error al fallar la carga de cotizaciones y permite reintentar', async () => {
    const user = userEvent.setup()
    let fallo = true

    server.use(
      http.get('*/api/cotizaciones', () => {
        if (fallo) {
          fallo = false
          return HttpResponse.json({ status: 500 }, { status: 500 })
        }
        return HttpResponse.json(
          [
            {
              cotizacionId: 'cot-1',
              licitacionId: LIC_ABIERTA_ID,
              proveedorId: 'prov-0001-guid',
              items: [],
              flete: 0,
              plazoEntregaDias: 2,
              fechaCreacion: '2026-10-22T00:00:00Z',
              puntajeTotal: 90,
              esGanadora: true,
            },
          ],
          { status: 200 },
        )
      }),
    )

    renderPagina(`/compras/licitaciones?licitacionId=${LIC_ABIERTA_ID}`)

    expect(
      await screen.findByText('No se pudieron cargar las cotizaciones. Intentá nuevamente.'),
    ).toBeInTheDocument()

    const botonReintentar = screen.getByRole('button', { name: 'Reintentar' })
    await user.click(botonReintentar)

    expect(await screen.findByText('prov-000')).toBeInTheDocument()
  })

  it('solicita confirmación previa antes de adjudicar y permite cancelar sin enviar la petición', async () => {
    const user = userEvent.setup()
    let peticionEnviada = false

    server.use(
      http.post('*/api/licitaciones/:licitacionId/adjudicar', () => {
        peticionEnviada = true
        return HttpResponse.json({}, { status: 200 })
      }),
    )

    renderPagina(`/compras/licitaciones?licitacionId=${LIC_ABIERTA_ID}`)

    await screen.findByText('f7a8b9c0')

    const botonAdjudicar = screen.getByRole('button', {
      name: 'Adjudicar a proveedor f7a8b9c0',
    })
    await user.click(botonAdjudicar)

    // Aparece la confirmación
    expect(
      screen.getByText('¿Confirmás la adjudicación a este proveedor?'),
    ).toBeInTheDocument()
    const botonCancelar = screen.getByRole('button', { name: 'Cancelar' })
    const botonConfirmar = screen.getByRole('button', { name: 'Confirmar adjudicación' })

    expect(botonCancelar).toBeInTheDocument()
    expect(botonConfirmar).toBeInTheDocument()

    // Cancelar
    await user.click(botonCancelar)

    expect(
      screen.queryByText('¿Confirmás la adjudicación a este proveedor?'),
    ).not.toBeInTheDocument()
    expect(peticionEnviada).toBe(false)
  })

  it('adjudica con éxito, envía la petición y muestra el mensaje con la Orden de Compra emitida', async () => {
    const user = userEvent.setup()
    let cuerpoEnviado: unknown = null

    server.use(
      http.post('*/api/licitaciones/:licitacionId/adjudicar', async ({ request }) => {
        cuerpoEnviado = await request.json()
        return HttpResponse.json(
          {
            id: 'OC-2026-999',
            licitacionId: LIC_ABIERTA_ID,
            cotizacionId: 'c1111111-0000-0000-0000-000000000001',
            estado: 'Emitida',
            fechaEmision: new Date().toISOString(),
          },
          { status: 200 },
        )
      }),
    )

    renderPagina(`/compras/licitaciones?licitacionId=${LIC_ABIERTA_ID}`)

    await screen.findByText('f7a8b9c0')

    const botonAdjudicar = screen.getByRole('button', {
      name: 'Adjudicar a proveedor f7a8b9c0',
    })
    await user.click(botonAdjudicar)

    const botonConfirmar = screen.getByRole('button', { name: 'Confirmar adjudicación' })
    await user.click(botonConfirmar)

    expect(
      await screen.findByText(
        '¡Licitación adjudicada con éxito! Se emitió la Orden de Compra OC-2026-999.',
      ),
    ).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Ver la orden de compra' })).toHaveAttribute(
      'href',
      '/compras/ordenes?ordenCompraId=OC-2026-999',
    )

    expect(cuerpoEnviado).toEqual({
      cotizacionId: 'c1111111-0000-0000-0000-000000000001',
    })

    // La licitación queda marcada como adjudicada
    expect(screen.getByText('Licitación ya adjudicada')).toBeInTheDocument()
  })

  it('conserva el éxito pero ofrece la lista cuando la respuesta no trae un identificador de orden válido', async () => {
    const user = userEvent.setup()
    server.use(http.post('*/api/licitaciones/:licitacionId/adjudicar', () => HttpResponse.json({
      id: ' ', licitacionId: LIC_ABIERTA_ID, cotizacionId: 'c1111111-0000-0000-0000-000000000001', estado: 'Emitida', fechaEmision: new Date().toISOString(),
    })))
    renderPagina(`/compras/licitaciones?licitacionId=${LIC_ABIERTA_ID}`)
    await user.click(await screen.findByRole('button', { name: 'Adjudicar a proveedor f7a8b9c0' }))
    await user.click(screen.getByRole('button', { name: 'Confirmar adjudicación' }))
    expect(await screen.findByText('¡Licitación adjudicada con éxito!')).toBeInTheDocument()
    expect(screen.getByText(/No recibimos un identificador válido/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Órdenes de compra' })).toHaveAttribute('href', '/compras/ordenes')
    expect(screen.queryByRole('link', { name: 'Ver la orden de compra' })).not.toBeInTheDocument()
  })

  it('muestra mensaje del ProblemDetails cuando el servidor rechaza la adjudicación (error 409 conflicto)', async () => {
    const user = userEvent.setup()

    server.use(
      http.post('*/api/licitaciones/:licitacionId/adjudicar', () => {
        return HttpResponse.json(
          {
            type: 'https://tools.ietf.org/html/rfc7231#section-6.5.8',
            title: 'Conflicto en la adjudicación',
            status: 409,
            detail: 'La licitación ya se encuentra adjudicada o cerrada.',
          },
          { status: 409 },
        )
      }),
    )

    renderPagina(`/compras/licitaciones?licitacionId=${LIC_ABIERTA_ID}`)

    await screen.findByText('f7a8b9c0')

    await user.click(screen.getByRole('button', { name: 'Adjudicar a proveedor f7a8b9c0' }))
    await user.click(screen.getByRole('button', { name: 'Confirmar adjudicación' }))

    expect(
      await screen.findByText('La licitación ya se encuentra adjudicada o cerrada.'),
    ).toBeInTheDocument()
  })

  it('la protección contra doble clic no envía múltiples peticiones simultáneas', async () => {
    const user = userEvent.setup()
    let llamadas = 0

    server.use(
      http.post('*/api/licitaciones/:licitacionId/adjudicar', async () => {
        llamadas++
        await delay(100)
        return HttpResponse.json(
          {
            id: 'OC-TEST-001',
            licitacionId: LIC_ABIERTA_ID,
            cotizacionId: 'c1111111-0000-0000-0000-000000000001',
            estado: 'Emitida',
            fechaEmision: new Date().toISOString(),
          },
          { status: 200 },
        )
      }),
    )

    renderPagina(`/compras/licitaciones?licitacionId=${LIC_ABIERTA_ID}`)

    await screen.findByText('f7a8b9c0')

    await user.click(screen.getByRole('button', { name: 'Adjudicar a proveedor f7a8b9c0' }))
    const botonConfirmar = screen.getByRole('button', { name: 'Confirmar adjudicación' })

    await Promise.all([user.click(botonConfirmar), user.click(botonConfirmar)])

    expect(
      await screen.findByText(
        '¡Licitación adjudicada con éxito! Se emitió la Orden de Compra OC-TEST-001.',
      ),
    ).toBeInTheDocument()

    expect(llamadas).toBe(1)
  })

  it('al elegir una licitación baja a sus cotizaciones y deja el foco en el título', async () => {
    const user = userEvent.setup()
    const scrollIntoView = vi.fn()
    Element.prototype.scrollIntoView = scrollIntoView
    renderPagina()

    const botones = await screen.findAllByRole('button', { name: 'Ver cotizaciones' })
    await user.click(botones[0])

    const titulo = await screen.findByRole('heading', { level: 2, name: /^Cotizaciones para / })
    expect(titulo).toHaveFocus()
    expect(scrollIntoView).toHaveBeenCalledTimes(1)
  })

  it('si la licitación llega elegida desde la URL no se mueve la pantalla sola', async () => {
    const scrollIntoView = vi.fn()
    Element.prototype.scrollIntoView = scrollIntoView
    renderPagina(`/compras/licitaciones?licitacionId=${LIC_ABIERTA_ID}`)

    await screen.findByRole('heading', { level: 2, name: /^Cotizaciones para / })

    expect(scrollIntoView).not.toHaveBeenCalled()
  })

  it('muestra cada cotización como una tarjeta con precio total, flete, plazo, puntaje y su botón Adjudicar', async () => {
    renderPagina(`/compras/licitaciones?licitacionId=${LIC_ABIERTA_ID}`)

    const lista = await screen.findByRole('list', { name: /^Cotizaciones recibidas para / })
    // Solo las tarjetas (hijas directas de la lista): adentro hay otras listas, de marcas y de desglose.
    const tarjetas = Array.from(lista.children) as HTMLElement[]

    expect(tarjetas.length).toBeGreaterThan(0)
    for (const tarjeta of tarjetas) {
      expect(within(tarjeta).getByText('Precio total')).toBeInTheDocument()
      expect(within(tarjeta).getByText('Flete')).toBeInTheDocument()
      expect(within(tarjeta).getByText('Plazo de entrega')).toBeInTheDocument()
      expect(within(tarjeta).getByText('Calificación de la oferta')).toBeInTheDocument()
      expect(
        within(tarjeta).getByRole('button', { name: /^Adjudicar a proveedor / }),
      ).toBeInTheDocument()
    }
  })

  it('arriba de las ofertas resume qué se pidió, hasta cuándo se cotiza y cuántas ofertas llegaron', async () => {
    renderPagina(`/compras/licitaciones?licitacionId=${LIC_ABIERTA_ID}`)

    const resumen = await screen.findByRole('region', { name: 'Resumen del pliego' })

    expect(within(resumen).getByText(/80 bolsas de Cemento Portland Especial 25kg/)).toBeInTheDocument()
    expect(within(resumen).getByText(/15 metros cúbicos \(m³\) de Arena Fina a Granel/)).toBeInTheDocument()
    expect(within(resumen).getByText(/Cotizan hasta el 20 de noviembre de 2026/)).toBeInTheDocument()
    expect(await within(resumen).findByText('2 ofertas recibidas')).toBeInTheDocument()
  })

  it('marca la mejor oferta en precio, flete y plazo y no marca a la otra', async () => {
    renderPagina(`/compras/licitaciones?licitacionId=${LIC_ABIERTA_ID}`)

    const lista = await screen.findByRole('list', { name: /^Cotizaciones recibidas para / })
    const [primera, segunda] = Array.from(lista.children) as HTMLElement[]

    for (const marca of ['Más barata', 'Flete más bajo', 'Entrega más rápida']) {
      expect(within(primera).getByText(marca)).toBeInTheDocument()
      expect(within(segunda).queryByText(marca)).not.toBeInTheDocument()
    }
  })

  it('cada oferta muestra el total con flete y la fecha en que se cotizó', async () => {
    renderPagina(`/compras/licitaciones?licitacionId=${LIC_ABIERTA_ID}`)

    const lista = await screen.findByRole('list', { name: /^Cotizaciones recibidas para / })

    // 850.000 de materiales + 15.000 de flete de la primera oferta.
    expect(within(lista).getByText('$ 865.000')).toBeInTheDocument()
    expect(within(lista).getByText('Cotizó el 22 de octubre de 2026')).toBeInTheDocument()
    expect(within(lista).getByText('Cotizó el 23 de octubre de 2026')).toBeInTheDocument()
  })

  it('el desglose por ítem muestra cantidad, precio unitario y subtotal de cada material', async () => {
    const user = userEvent.setup()
    renderPagina(`/compras/licitaciones?licitacionId=${LIC_ABIERTA_ID}`)

    const lista = await screen.findByRole('list', { name: /^Cotizaciones recibidas para / })
    await user.click(within(lista).getAllByText('Ver detalle por ítem')[0])

    expect(within(lista).getAllByText('$ 8.000 c/u = $ 640.000').length).toBeGreaterThan(0)
    expect(within(lista).getAllByText('$ 14.000 c/u = $ 210.000').length).toBeGreaterThan(0)
  })

  describe('datos que el servicio puede sumar al contrato (nombre, calificación y desglose)', () => {
    const COTIZACION_COMPLETA = {
      cotizacionId: 'c9999999-0000-0000-0000-000000000009',
      licitacionId: LIC_ABIERTA_ID,
      proveedorId: 'a1b2c3d4-0000-0000-0000-000000000001',
      proveedorNombre: 'Corralón San Martín',
      calificacionProveedor: 4.5,
      flete: 10000,
      plazoEntregaDias: 3,
      fechaCreacion: '2026-10-22T10:00:00Z',
      items: [{ articuloId: 'c1c2c3c4-0000-0000-0000-000000000001', precioUnitario: 8000 }],
      puntajeTotal: 80,
      esGanadora: false,
      desglosePuntaje: { precio: 40, flete: 15, plazo: 15, calificacion: 10 },
      ponderacionesScoring: { precio: 0.5, flete: 0.2, plazo: 0.2, calificacion: 0.1 },
    }

    it('muestra el nombre del proveedor, su calificación y cómo se calculó el puntaje', async () => {
      const user = userEvent.setup()
      server.use(http.get('*/api/cotizaciones', () => HttpResponse.json([COTIZACION_COMPLETA])))
      renderPagina(`/compras/licitaciones?licitacionId=${LIC_ABIERTA_ID}`)

      const lista = await screen.findByRole('list', { name: /^Cotizaciones recibidas para / })
      expect(within(lista).getByText('Corralón San Martín')).toBeInTheDocument()
      // Una vez como dato de la tarjeta y otra como criterio del desglose.
      expect(within(lista).getAllByText('Calificación del proveedor')).toHaveLength(2)
      expect(within(lista).getByText('4,5')).toBeInTheDocument()
      expect(
        within(lista).getByRole('button', { name: 'Adjudicar a proveedor Corralón San Martín' }),
      ).toBeInTheDocument()

      await user.click(within(lista).getByText('Calificación por criterio'))
      expect(within(lista).getByText('40 pts')).toBeInTheDocument()
      expect(within(lista).getByText('10 pts')).toBeInTheDocument()
    })

    it('con el desglose del servicio muestra estrellas por criterio, sin porcentajes', async () => {
      const user = userEvent.setup()
      const conDesglose = {
        ...COTIZACION_COMPLETA,
        desgloseScoring: {
          puntajePrecio: 100,
          puntajeFlete: 80,
          puntajePlazo: 50,
          puntajeCalificacion: 9,
          pesoPrecio: 0.5,
          pesoFlete: 0.2,
          pesoPlazo: 0.2,
          pesoCalificacion: 0.1,
        },
      }
      server.use(http.get('*/api/cotizaciones', () => HttpResponse.json([conDesglose])))
      renderPagina(`/compras/licitaciones?licitacionId=${LIC_ABIERTA_ID}`)

      const lista = await screen.findByRole('list', { name: /^Cotizaciones recibidas para / })
      await user.click(within(lista).getByText('Calificación por criterio'))

      // 100 → 5 estrellas, 80 → 4, 50 → 3 (2,5 redondea hacia arriba), 9 → 0.
      expect(within(lista).getByRole('img', { name: '5 de 5 estrellas' })).toBeInTheDocument()
      expect(within(lista).getByRole('img', { name: '4 de 5 estrellas' })).toBeInTheDocument()
      expect(within(lista).getByRole('img', { name: '3 de 5 estrellas' })).toBeInTheDocument()
      expect(within(lista).getByRole('img', { name: '0 de 5 estrellas' })).toBeInTheDocument()
      expect(within(lista).queryByText(/peso \d+ %/)).not.toBeInTheDocument()
      // Con estrellas ya no se muestran los puntos sueltos.
      expect(within(lista).queryByText('40 pts')).not.toBeInTheDocument()
    })

    it('sin esos datos muestra el inicio del identificador y no inventa calificación ni desglose', async () => {
      renderPagina(`/compras/licitaciones?licitacionId=${LIC_ABIERTA_ID}`)

      const lista = await screen.findByRole('list', { name: /^Cotizaciones recibidas para / })

      expect(within(lista).getByText('f7a8b9c0')).toBeInTheDocument()
      expect(within(lista).queryByText('Calificación del proveedor')).not.toBeInTheDocument()
      expect(within(lista).queryByText('Calificación por criterio')).not.toBeInTheDocument()
    })
  })

  describe('ofertas con otra presentación (#229)', () => {
    const ART_CEMENTO = 'c1c2c3c4-0000-0000-0000-000000000001'
    const ART_ARENA = 'c1c2c3c4-0000-0000-0000-000000000002'
    const OFERTA_ALTERNATIVA = {
      cotizacionId: 'c9999999-0000-0000-0000-000000000009',
      licitacionId: LIC_ABIERTA_ID,
      proveedorId: 'a1b2c3d4-0000-0000-0000-000000000001',
      flete: 10000,
      plazoEntregaDias: 3,
      fechaCreacion: '2026-10-22T10:00:00Z',
      coberturaParcial: true,
      items: [
        {
          articuloId: ART_CEMENTO,
          precioUnitario: 1000,
          presentacionOfrecida: 'bolsa 25 kg',
          cantidadOfrecida: 2,
          subtotalItem: 2000,
          coberturaParcial: true,
        },
        { articuloId: ART_ARENA, precioUnitario: 14000 },
      ],
      puntajeTotal: 80,
      esGanadora: false,
    }

    it('muestra las insignias, lo que ofrece por ítem y avisa que no cubre todo lo pedido', async () => {
      server.use(http.get('*/api/cotizaciones', () => HttpResponse.json([OFERTA_ALTERNATIVA])))

      renderPagina(`/compras/licitaciones?licitacionId=${LIC_ABIERTA_ID}`)

      const lista = await screen.findByRole('list', { name: /^Cotizaciones recibidas para / })
      expect(within(lista).getByText('Presentación distinta')).toBeInTheDocument()
      expect(within(lista).getByText('Cobertura parcial')).toBeInTheDocument()

      // El detalle ya viene abierto en las ofertas distintas: Compras lo ve sin tocar nada.
      expect(within(lista).getByText('Ofrece: 2 × bolsa 25 kg')).toBeVisible()
      expect(within(lista).getByText('$ 1.000 c/u = $ 2.000')).toBeVisible()
      expect(within(lista).getByText('$ 14.000 c/u = $ 210.000')).toBeVisible()
      expect(within(lista).getByText('Esto no cubre todo lo pedido.')).toBeVisible()
    })

    it('el precio total es lo que ofrece (2 bolsas + la arena), no la cantidad del pliego', async () => {
      server.use(http.get('*/api/cotizaciones', () => HttpResponse.json([OFERTA_ALTERNATIVA])))

      renderPagina(`/compras/licitaciones?licitacionId=${LIC_ABIERTA_ID}`)

      const lista = await screen.findByRole('list', { name: /^Cotizaciones recibidas para / })
      expect(within(lista).getByText('$ 212.000')).toBeInTheDocument()
    })

    it('no compara con la última compra un precio que es de otra presentación', async () => {
      server.use(http.get('*/api/cotizaciones', () => HttpResponse.json([OFERTA_ALTERNATIVA])))

      renderPagina(`/compras/licitaciones?licitacionId=${LIC_ABIERTA_ID}`)

      const lista = await screen.findByRole('list', { name: /^Cotizaciones recibidas para / })
      await within(lista).findByText('Ofrece: 2 × bolsa 25 kg')
      expect(within(lista).queryByText(/que la última compra/)).not.toBeInTheDocument()
    })

    it('no se descarta la oferta: se la puede adjudicar igual', async () => {
      server.use(http.get('*/api/cotizaciones', () => HttpResponse.json([OFERTA_ALTERNATIVA])))

      renderPagina(`/compras/licitaciones?licitacionId=${LIC_ABIERTA_ID}`)

      const lista = await screen.findByRole('list', { name: /^Cotizaciones recibidas para / })
      expect(within(lista).getByRole('button', { name: /^Adjudicar a proveedor / })).toBeEnabled()
    })

    it('las ofertas normales no llevan insignias', async () => {
      renderPagina(`/compras/licitaciones?licitacionId=${LIC_ABIERTA_ID}`)

      const lista = await screen.findByRole('list', { name: /^Cotizaciones recibidas para / })
      expect(within(lista).queryByText('Presentación distinta')).not.toBeInTheDocument()
      expect(within(lista).queryByText('Cobertura parcial')).not.toBeInTheDocument()
    })
  })

  it('usa el ancho grande de la PC (pantalla-ancha), como las demás pantallas con listas', () => {
    const { container } = renderPagina()

    expect(container.querySelector('section.pantalla-ancha')).not.toBeNull()
  })
})
