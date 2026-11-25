import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { delay, http, HttpResponse } from 'msw'
import { MemoryRouter, Route, Routes } from 'react-router'
import { beforeEach, describe, expect, it } from 'vitest'
import { AuthContext, type SesionUsuario } from '../auth/sessionContext'
import { reiniciarLicitacionesMock } from '../mocks/handlers'
import { server } from '../mocks/server'
import { CotizarLicitacionPage } from './CotizarLicitacionPage'

const proveedor: SesionUsuario = {
  id: 'a1b2c3d4-0000-0000-0000-000000000003',
  nombreCompleto: 'Carlos Ruiz',
  email: 'proveedor@obrasync.com',
  rol: 'Proveedor',
}

const LIC_ABIERTA_ID = 'b1b2c3d4-1111-0000-0000-000000000001'

function renderPagina(
  licitacionId = LIC_ABIERTA_ID,
  usuario: SesionUsuario = proveedor,
  busqueda = '',
) {
  return render(
    <AuthContext.Provider value={{ usuario, login: () => {}, logout: () => {} }}>
      <MemoryRouter initialEntries={[`/proveedores/licitaciones/${licitacionId}${busqueda}`]}>
        <Routes>
          <Route
            path="/proveedores/licitaciones/:id"
            element={<CotizarLicitacionPage />}
          />
        </Routes>
      </MemoryRouter>
    </AuthContext.Provider>,
  )
}

describe('CotizarLicitacionPage', () => {
  beforeEach(() => {
    reiniciarLicitacionesMock()
  })

  it('Admin no ve el formulario ni puede iniciar una cotización desde la ruta directa', async () => {
    renderPagina(LIC_ABIERTA_ID, { ...proveedor, rol: 'Admin' })

    expect(await screen.findByRole('alert')).toHaveTextContent('Solo un proveedor puede cotizar esta licitación.')
    expect(screen.queryByRole('button', { name: 'Enviar cotización' })).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Costo de flete ($)')).not.toBeInTheDocument()
  })

  it('muestra un mensaje claro ante 403 ProblemDetails al enviar la cotización', async () => {
    const user = userEvent.setup()
    server.use(http.post('*/api/cotizaciones', () => HttpResponse.json(
      { title: 'Forbidden', status: 403, detail: 'User role not authorized' },
      { status: 403, headers: { 'Content-Type': 'application/problem+json' } },
    )))
    renderPagina()
    await screen.findByText('Cotizar REQ-2026-0011')
    const precios = screen.getAllByLabelText('Precio unitario ($)')
    await user.type(precios[0], '1000')
    await user.type(precios[1], '350')
    await user.type(screen.getByLabelText('Costo de flete ($)'), '0')
    await user.type(screen.getByLabelText('Plazo de entrega (en días)'), '5')
    await user.click(screen.getByRole('button', { name: 'Enviar cotización' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Solo los proveedores pueden realizar esta acción.')
    expect(screen.getByRole('alert')).not.toHaveTextContent('User role not authorized')
  })

  it('el plazo de entrega ofrece opciones frecuentes: tocar una completa el campo y se puede cambiar a mano', async () => {
    const user = userEvent.setup()
    renderPagina()
    await screen.findByText('Cotizar REQ-2026-0011')

    const campo = screen.getByLabelText('Plazo de entrega (en días)')
    await user.click(screen.getByRole('button', { name: '7 días' }))
    expect(campo).toHaveValue('7')
    expect(screen.getByRole('button', { name: '7 días' })).toHaveAttribute('aria-pressed', 'true')

    await user.clear(campo)
    await user.type(campo, '12')
    expect(screen.getByRole('button', { name: '7 días' })).toHaveAttribute('aria-pressed', 'false')

    await user.click(screen.getByRole('button', { name: '1 día' }))
    expect(campo).toHaveValue('1')
  })

  it('no envía el formulario con datos inválidos y muestra errores de validación', async () => {
    const user = userEvent.setup()
    let peticionEnviada = false

    server.use(
      http.post('*/api/cotizaciones', () => {
        peticionEnviada = true
        return HttpResponse.json({}, { status: 201 })
      }),
    )

    renderPagina()

    expect(await screen.findByText('Cotizar REQ-2026-0011')).toBeInTheDocument()

    const botonEnviar = screen.getByRole('button', { name: 'Enviar cotización' })
    await user.click(botonEnviar)

    expect(await screen.findAllByText('Ingresá el precio unitario.')).toHaveLength(2)
    expect(screen.getByText('Ingresá el costo de flete.')).toBeInTheDocument()
    expect(screen.getByText('Ingresá el plazo de entrega.')).toBeInTheDocument()
    expect(peticionEnviada).toBe(false)
  })

  it('valida al salir del campo (onBlur)', async () => {
    const user = userEvent.setup()
    renderPagina()

    await screen.findByText('Cotizar REQ-2026-0011')

    const inputFlete = screen.getByLabelText('Costo de flete ($)')
    await user.click(inputFlete)
    await user.type(inputFlete, '-10')
    await user.tab()

    expect(await screen.findByText('El flete no puede ser negativo.')).toBeInTheDocument()

    const inputPlazo = screen.getByLabelText('Plazo de entrega (en días)')
    await user.click(inputPlazo)
    await user.type(inputPlazo, '3.5')
    await user.tab()

    expect(
      await screen.findByText('El plazo de entrega debe ser un número entero de días.'),
    ).toBeInTheDocument()
  })

  it('no deja escribir comas en precio, flete ni plazo, y rechaza plazos de más de 15 días', async () => {
    const user = userEvent.setup()
    renderPagina()
    await screen.findByText('Cotizar REQ-2026-0011')

    const precio = screen.getAllByLabelText('Precio unitario ($)')[0]
    const flete = screen.getByLabelText('Costo de flete ($)')
    const plazo = screen.getByLabelText('Plazo de entrega (en días)')
    await user.type(precio, '1200,50')
    await user.type(flete, '500,5')
    await user.type(plazo, '1,5')

    // La coma no aparece: queda lo demás tal cual se tipeó.
    expect(precio).toHaveValue('120050')
    expect(flete).toHaveValue('5005')
    expect(plazo).toHaveValue('15')

    await user.clear(plazo)
    await user.type(plazo, '16')
    await user.tab()
    expect(
      await screen.findByText('El plazo de entrega no puede superar los 15 días, contados desde que cotizás.'),
    ).toBeInTheDocument()
  })

  it('envía el cuerpo correcto con proveedorId y datos parseados', async () => {
    const user = userEvent.setup()
    let cuerpoEnviado: unknown = null

    server.use(
      http.post('*/api/cotizaciones', async ({ request }) => {
        cuerpoEnviado = await request.json()
        return HttpResponse.json(
          {
            cotizacionId: 'cot-123',
            licitacionId: LIC_ABIERTA_ID,
            proveedorId: proveedor.id,
            fechaCreacion: new Date().toISOString(),
            items: [],
          },
          { status: 201 },
        )
      }),
    )

    renderPagina()

    await screen.findByText('Cotizar REQ-2026-0011')

    const inputsPrecio = screen.getAllByLabelText('Precio unitario ($)')
    await user.type(inputsPrecio[0], '1200.50')
    await user.type(inputsPrecio[1], '350')

    const inputFlete = screen.getByLabelText('Costo de flete ($)')
    await user.type(inputFlete, '500.00')

    const inputPlazo = screen.getByLabelText('Plazo de entrega (en días)')
    await user.type(inputPlazo, '10')

    await user.click(screen.getByRole('button', { name: 'Enviar cotización' }))

    expect(await screen.findByText('¡Cotización enviada con éxito!')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Volver a licitaciones' })).toHaveAttribute(
      'href',
      '/proveedores',
    )

    expect(cuerpoEnviado).toEqual({
      licitacionId: LIC_ABIERTA_ID,
      proveedorId: proveedor.id,
      flete: 500,
      plazoEntregaDias: 10,
      items: [
        {
          articuloId: 'c1c2c3c4-0000-0000-0000-000000000001',
          precioUnitario: 1200.5,
        },
        {
          articuloId: 'c1c2c3c4-0000-0000-0000-000000000002',
          precioUnitario: 350,
        },
      ],
    })
  })

  it('muestra mensaje si ya cotizó y deja la pantalla estable con enlace para volver', async () => {
    const user = userEvent.setup()

    server.use(
      http.post('*/api/cotizaciones', () => {
        return HttpResponse.json(
          {
            title: 'Cotización duplicada',
            status: 409,
            detail: 'Ya cotizaste para esta licitación.',
          },
          { status: 409 },
        )
      }),
    )

    renderPagina()

    await screen.findByText('Cotizar REQ-2026-0011')

    const inputsPrecio = screen.getAllByLabelText('Precio unitario ($)')
    await user.type(inputsPrecio[0], '1000')
    await user.type(inputsPrecio[1], '200')
    await user.type(screen.getByLabelText('Costo de flete ($)'), '0')
    await user.type(screen.getByLabelText('Plazo de entrega (en días)'), '5')

    await user.click(screen.getByRole('button', { name: 'Enviar cotización' }))

    expect(
      await screen.findByText('Ya cotizaste para esta licitación.'),
    ).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Enviar cotización' })).toBeDisabled()
    expect(screen.getByRole('link', { name: 'Volver a licitaciones' })).toBeInTheDocument()
  })

  it('muestra mensaje si la licitación está cerrada y deja la pantalla estable', async () => {
    const user = userEvent.setup()

    server.use(
      http.post('*/api/cotizaciones', () => {
        return HttpResponse.json(
          {
            title: 'Licitación cerrada',
            status: 400,
            detail: 'La licitación ya se encuentra cerrada y no recibe cotizaciones.',
          },
          { status: 400 },
        )
      }),
    )

    renderPagina()

    await screen.findByText('Cotizar REQ-2026-0011')

    const inputsPrecio = screen.getAllByLabelText('Precio unitario ($)')
    await user.type(inputsPrecio[0], '1000')
    await user.type(inputsPrecio[1], '200')
    await user.type(screen.getByLabelText('Costo de flete ($)'), '100')
    await user.type(screen.getByLabelText('Plazo de entrega (en días)'), '3')

    await user.click(screen.getByRole('button', { name: 'Enviar cotización' }))

    expect(
      await screen.findByText(
        'La licitación ya se encuentra cerrada y no recibe cotizaciones.',
      ),
    ).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Enviar cotización' })).toBeDisabled()
    expect(screen.getByRole('link', { name: 'Volver a licitaciones' })).toBeInTheDocument()
  })

  it('la protección contra doble clic envía una sola petición', async () => {
    const user = userEvent.setup()
    let llamadas = 0

    server.use(
      http.post('*/api/cotizaciones', async () => {
        llamadas++
        await delay(100)
        return HttpResponse.json(
          {
            cotizacionId: 'cot-999',
            licitacionId: LIC_ABIERTA_ID,
            proveedorId: proveedor.id,
            fechaCreacion: new Date().toISOString(),
            items: [],
          },
          { status: 201 },
        )
      }),
    )

    renderPagina()

    await screen.findByText('Cotizar REQ-2026-0011')

    const inputsPrecio = screen.getAllByLabelText('Precio unitario ($)')
    await user.type(inputsPrecio[0], '1000')
    await user.type(inputsPrecio[1], '200')
    await user.type(screen.getByLabelText('Costo de flete ($)'), '0')
    await user.type(screen.getByLabelText('Plazo de entrega (en días)'), '5')

    const boton = screen.getByRole('button', { name: 'Enviar cotización' })
    await Promise.all([user.click(boton), user.click(boton)])

    expect(await screen.findByText('¡Cotización enviada con éxito!')).toBeInTheDocument()
    expect(llamadas).toBe(1)
  })

  it('informa error 404 cuando la licitación no existe y ofrece volver', async () => {
    renderPagina('licitacion-inexistente')

    expect(await screen.findByText('La licitación ya no existe')).toBeInTheDocument()
    expect(
      screen.getByText('El pliego que buscás no se encuentra disponible o fue eliminado.'),
    ).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Volver a licitaciones' })).toHaveAttribute(
      'href',
      '/proveedores',
    )
  })

  it('con ?corregir carga la oferta anterior y la guarda con PUT', async () => {
    const user = userEvent.setup()
    let putUrl = ''
    let putBody: { flete?: number; items?: Array<{ precioUnitario: number }> } = {}

    server.use(
      http.get('*/api/cotizaciones/mias', () =>
        HttpResponse.json([
          {
            cotizacionId: 'c9999999-0000-0000-0000-000000000009',
            licitacionId: LIC_ABIERTA_ID,
            proveedorId: proveedor.id,
            numeroRequisicion: 'REQ-2026-0011',
            obraId: 'o1',
            fechaLimiteCotizacion: '2099-01-01T12:00:00Z',
            estadoLicitacion: 'Abierta',
            subtotalItems: 0,
            flete: 7000,
            costoTotal: 7000,
            coberturaParcial: false,
            plazoEntregaDias: 4,
            fechaCreacion: '2026-11-10T12:00:00Z',
            items: [],
            esGanadora: false,
            estadoResultado: 'Enviada',
          },
        ]),
      ),
      http.put('*/api/cotizaciones/:id', async ({ request }) => {
        putUrl = request.url
        putBody = (await request.json()) as typeof putBody
        return HttpResponse.json({}, { status: 200 })
      }),
    )

    renderPagina(LIC_ABIERTA_ID, proveedor, '?corregir=c9999999-0000-0000-0000-000000000009')

    expect(await screen.findByRole('heading', { name: /Corregir cotización de/ })).toBeInTheDocument()
    expect(await screen.findByLabelText(/Costo de flete/)).toHaveValue('7000')

    for (const campo of screen.getAllByLabelText(/Precio unitario/)) {
      await user.clear(campo)
      await user.type(campo, '1000')
    }
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }))

    expect(await screen.findByText('¡Cotización corregida con éxito!')).toBeInTheDocument()
    expect(putUrl).toContain('/api/cotizaciones/c9999999-0000-0000-0000-000000000009')
    expect(putBody.flete).toBe(7000)
    expect(putBody.items?.every((i) => i.precioUnitario === 1000)).toBe(true)
  })

  describe('ofrecer otra presentación (#229)', () => {
    const ART_1 = 'c1c2c3c4-0000-0000-0000-000000000001'
    const ART_2 = 'c1c2c3c4-0000-0000-0000-000000000002'

    function responderCotizacionCreada() {
      return HttpResponse.json(
        {
          cotizacionId: 'cot-229',
          licitacionId: LIC_ABIERTA_ID,
          proveedorId: proveedor.id,
          fechaCreacion: new Date().toISOString(),
          items: [],
        },
        { status: 201 },
      )
    }

    async function completarFleteYPlazo(user: ReturnType<typeof userEvent.setup>) {
      await user.type(screen.getByLabelText('Costo de flete ($)'), '0')
      await user.type(screen.getByLabelText('Plazo de entrega (en días)'), '5')
    }

    it('por defecto no muestra los campos de otra presentación', async () => {
      renderPagina()
      await screen.findByText('Cotizar REQ-2026-0011')

      expect(screen.getAllByRole('button', { name: 'Ofrecer otra presentación' })).toHaveLength(2)
      expect(screen.queryByLabelText('Presentación')).not.toBeInTheDocument()
      expect(screen.getAllByLabelText('Precio unitario ($)')).toHaveLength(2)
    })

    it('cotiza un ítem con otra presentación y manda los campos nuevos solo en ese ítem', async () => {
      const user = userEvent.setup()
      let cuerpo: { items?: Array<Record<string, unknown>> } = {}
      server.use(
        http.post('*/api/cotizaciones', async ({ request }) => {
          cuerpo = (await request.json()) as typeof cuerpo
          return responderCotizacionCreada()
        }),
      )

      renderPagina()
      await screen.findByText('Cotizar REQ-2026-0011')

      const [primero] = screen.getAllByRole('button', { name: 'Ofrecer otra presentación' })
      await user.click(primero)
      await user.type(screen.getByLabelText('Presentación'), '  bolsa de 25 kg  ')
      await user.type(screen.getByLabelText('Cantidad de unidades'), '2')
      const preciosOtra = screen.getAllByLabelText('Precio unitario ($)')
      await user.type(preciosOtra[0], '1000')

      // El total que ofrece sale de cantidad ofrecida × precio por unidad ofrecida (contrato #190).
      expect(screen.getByText(/Total que ofrecés/)).toHaveTextContent(/2\.000/)

      await user.type(preciosOtra[1], '350')
      await completarFleteYPlazo(user)
      await user.click(screen.getByRole('button', { name: 'Enviar cotización' }))

      expect(await screen.findByText('¡Cotización enviada con éxito!')).toBeInTheDocument()
      expect(cuerpo.items?.[0]).toEqual({
        articuloId: ART_1,
        precioUnitario: 1000,
        presentacionOfrecida: 'bolsa de 25 kg',
        cantidadOfrecida: 2,
      })
      expect(cuerpo.items?.[1]).toEqual({ articuloId: ART_2, precioUnitario: 350 })
      expect(cuerpo.items?.[1]).not.toHaveProperty('presentacionOfrecida')
    })

    it('si la activa y la desactiva, el ítem se cotiza como lo pedido', async () => {
      const user = userEvent.setup()
      let cuerpo: { items?: Array<Record<string, unknown>> } = {}
      server.use(
        http.post('*/api/cotizaciones', async ({ request }) => {
          cuerpo = (await request.json()) as typeof cuerpo
          return responderCotizacionCreada()
        }),
      )

      renderPagina()
      await screen.findByText('Cotizar REQ-2026-0011')

      const [primero] = screen.getAllByRole('button', { name: 'Ofrecer otra presentación' })
      await user.click(primero)
      expect(screen.getByLabelText('Presentación')).toBeInTheDocument()
      await user.click(primero)
      expect(screen.queryByLabelText('Presentación')).not.toBeInTheDocument()

      const precios = screen.getAllByLabelText('Precio unitario ($)')
      await user.type(precios[0], '1000')
      await user.type(precios[1], '350')
      await completarFleteYPlazo(user)
      await user.click(screen.getByRole('button', { name: 'Enviar cotización' }))

      expect(await screen.findByText('¡Cotización enviada con éxito!')).toBeInTheDocument()
      expect(cuerpo.items?.[0]).toEqual({ articuloId: ART_1, precioUnitario: 1000 })
      expect(cuerpo.items?.[0]).not.toHaveProperty('cantidadOfrecida')
    })

    it('no envía si la otra presentación está activada pero incompleta o fuera de límites', async () => {
      const user = userEvent.setup()
      let peticionEnviada = false
      server.use(
        http.post('*/api/cotizaciones', () => {
          peticionEnviada = true
          return responderCotizacionCreada()
        }),
      )

      renderPagina()
      await screen.findByText('Cotizar REQ-2026-0011')

      const [primero] = screen.getAllByRole('button', { name: 'Ofrecer otra presentación' })
      await user.click(primero)
      const precios = screen.getAllByLabelText('Precio unitario ($)')
      await user.type(precios[0], '1000')
      await user.type(precios[1], '350')
      await completarFleteYPlazo(user)
      await user.click(screen.getByRole('button', { name: 'Enviar cotización' }))

      expect(
        await screen.findByText('Escribí qué presentación ofrecés (por ejemplo, bolsa de 25 kg).'),
      ).toBeInTheDocument()
      expect(screen.getByText('Ingresá la cantidad que ofrecés.')).toBeInTheDocument()

      // Fuera de los límites del contrato: más de 100 caracteres y cantidad menor a 0.01.
      await user.click(screen.getByLabelText('Presentación'))
      await user.paste('a'.repeat(101))
      await user.type(screen.getByLabelText('Cantidad de unidades'), '0')
      await user.tab()

      expect(await screen.findByText('La presentación no puede superar los 100 caracteres.')).toBeInTheDocument()
      expect(screen.getByText('La cantidad ofrecida debe ser al menos 0.01.')).toBeInTheDocument()
      expect(peticionEnviada).toBe(false)
    })

    it('avisa que no cubre todo lo pedido cuando el servicio marca cobertura parcial', async () => {
      const user = userEvent.setup()
      server.use(
        http.post('*/api/cotizaciones', () =>
          HttpResponse.json(
            {
              cotizacionId: 'cot-229',
              licitacionId: LIC_ABIERTA_ID,
              proveedorId: proveedor.id,
              fechaCreacion: new Date().toISOString(),
              coberturaParcial: true,
              items: [{ articuloId: ART_1, precioUnitario: 1000, coberturaParcial: true }],
            },
            { status: 201 },
          ),
        ),
      )

      renderPagina()
      await screen.findByText('Cotizar REQ-2026-0011')

      const precios = screen.getAllByLabelText('Precio unitario ($)')
      await user.type(precios[0], '1000')
      await user.type(precios[1], '350')
      await completarFleteYPlazo(user)
      await user.click(screen.getByRole('button', { name: 'Enviar cotización' }))

      expect(await screen.findByText('¡Cotización enviada con éxito!')).toBeInTheDocument()
      expect(screen.getByText('Esto no cubre todo lo pedido.')).toBeInTheDocument()
    })

    it('no muestra el aviso de cobertura parcial si el servicio no la marca', async () => {
      const user = userEvent.setup()
      server.use(http.post('*/api/cotizaciones', () => responderCotizacionCreada()))

      renderPagina()
      await screen.findByText('Cotizar REQ-2026-0011')

      const precios = screen.getAllByLabelText('Precio unitario ($)')
      await user.type(precios[0], '1000')
      await user.type(precios[1], '350')
      await completarFleteYPlazo(user)
      await user.click(screen.getByRole('button', { name: 'Enviar cotización' }))

      expect(await screen.findByText('¡Cotización enviada con éxito!')).toBeInTheDocument()
      expect(screen.queryByText('Esto no cubre todo lo pedido.')).not.toBeInTheDocument()
    })

    it('al corregir carga la otra presentación ofrecida y la vuelve a guardar con PUT', async () => {
      const user = userEvent.setup()
      let putBody: { items?: Array<Record<string, unknown>> } = {}
      server.use(
        http.get('*/api/cotizaciones/mias', () =>
          HttpResponse.json([
            {
              cotizacionId: 'c9999999-0000-0000-0000-000000000009',
              licitacionId: LIC_ABIERTA_ID,
              proveedorId: proveedor.id,
              numeroRequisicion: 'REQ-2026-0011',
              obraId: 'o1',
              fechaLimiteCotizacion: '2099-01-01T12:00:00Z',
              estadoLicitacion: 'Abierta',
              subtotalItems: 1800,
              flete: 7000,
              costoTotal: 8800,
              coberturaParcial: true,
              plazoEntregaDias: 4,
              fechaCreacion: '2026-11-10T12:00:00Z',
              items: [
                {
                  articuloId: ART_1,
                  precioUnitario: 900,
                  presentacionOfrecida: 'bolsa 25 kg',
                  cantidadOfrecida: 2,
                  subtotalItem: 1800,
                  coberturaParcial: true,
                },
              ],
              esGanadora: false,
              estadoResultado: 'Enviada',
            },
          ]),
        ),
        http.put('*/api/cotizaciones/:id', async ({ request }) => {
          putBody = (await request.json()) as typeof putBody
          return HttpResponse.json({}, { status: 200 })
        }),
      )

      renderPagina(LIC_ABIERTA_ID, proveedor, '?corregir=c9999999-0000-0000-0000-000000000009')

      expect(await screen.findByLabelText('Presentación')).toHaveValue('bolsa 25 kg')
      expect(screen.getByLabelText('Cantidad de unidades')).toHaveValue('2')
      const preciosCorregir = screen.getAllByLabelText('Precio unitario ($)')
      expect(preciosCorregir[0]).toHaveValue('900')
      expect(screen.getByText('Esto no cubre todo lo pedido.')).toBeInTheDocument()

      await user.type(preciosCorregir[1], '300')
      await user.click(screen.getByRole('button', { name: 'Guardar cambios' }))

      expect(await screen.findByText('¡Cotización corregida con éxito!')).toBeInTheDocument()
      expect(putBody.items?.[0]).toEqual({
        articuloId: ART_1,
        precioUnitario: 900,
        presentacionOfrecida: 'bolsa 25 kg',
        cantidadOfrecida: 2,
      })
    })
  })
})
