import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { delay, http, HttpResponse } from 'msw'
import { MemoryRouter, Route, Routes } from 'react-router'
import { beforeEach, describe, expect, it } from 'vitest'
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

  it('tiene enlace para volver a compras', () => {
    renderPagina()
    const enlaceVolver = screen.getByRole('link', { name: '← Volver a compras' })
    expect(enlaceVolver).toBeInTheDocument()
    expect(enlaceVolver).toHaveAttribute('href', '/compras')
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

    expect(
      await screen.findByText('Cotizaciones para REQ-2026-0011'),
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
    expect(screen.getByText('92.5 pts')).toBeInTheDocument()
    expect(screen.getByText('78 pts')).toBeInTheDocument()

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

    expect(cuerpoEnviado).toEqual({
      cotizacionId: 'c1111111-0000-0000-0000-000000000001',
    })

    // La licitación queda marcada como adjudicada
    expect(screen.getByText('Licitación ya adjudicada')).toBeInTheDocument()
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
})
