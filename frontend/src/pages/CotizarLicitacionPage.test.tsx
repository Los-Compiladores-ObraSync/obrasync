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
) {
  return render(
    <AuthContext.Provider value={{ usuario, login: () => {}, logout: () => {} }}>
      <MemoryRouter initialEntries={[`/proveedores/licitaciones/${licitacionId}`]}>
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

    const inputPlazo = screen.getByLabelText('Plazo de entrega (días)')
    await user.click(inputPlazo)
    await user.type(inputPlazo, '3,5')
    await user.tab()

    expect(
      await screen.findByText('El plazo de entrega debe ser un número entero de días.'),
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
    await user.type(inputsPrecio[0], '1200,50')
    await user.type(inputsPrecio[1], '350')

    const inputFlete = screen.getByLabelText('Costo de flete ($)')
    await user.type(inputFlete, '500,00')

    const inputPlazo = screen.getByLabelText('Plazo de entrega (días)')
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
    await user.type(screen.getByLabelText('Plazo de entrega (días)'), '5')

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
    await user.type(screen.getByLabelText('Plazo de entrega (días)'), '3')

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
    await user.type(screen.getByLabelText('Plazo de entrega (días)'), '5')

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
})
