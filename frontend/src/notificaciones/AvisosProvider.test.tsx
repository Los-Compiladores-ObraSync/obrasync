import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it } from 'vitest'
import { AuthContext, type SesionUsuario } from '../auth/sessionContext'
import { server } from '../mocks/server'
import { AvisosProvider } from './AvisosProvider'
import { claveVistosFlujo } from './avisosFlujo'
import { INTERVALO_REFRESCO_MS } from './configuracionAvisos'
import { leerVistos } from './novedades'
import { useAvisos } from './useAvisos'

const ORDEN_1 = '11111111-0000-0000-0000-000000000001'
const ORDEN_2 = '22222222-0000-0000-0000-000000000002'

function Avisos() {
  const { cantidad, paraHacer, novedades, marcarVistas, refrescar } = useAvisos()
  return (
    <>
      <p data-testid="cantidad">{cantidad}</p>
      <p data-testid="para-hacer">{paraHacer.map((a) => a.id).join(',')}</p>
      <p data-testid="novedades">{novedades.map((a) => a.id).join(',')}</p>
      <button type="button" onClick={() => marcarVistas()}>
        Marcar todas
      </button>
      <button type="button" onClick={() => refrescar()}>
        Refrescar ahora
      </button>
    </>
  )
}

function renderizar(rol: string, intervaloMs?: number) {
  const usuario: SesionUsuario = {
    id: 'u-1',
    nombreCompleto: 'Persona de Prueba',
    email: 'prueba@obrasync.com',
    rol,
  }
  return render(
    <AuthContext.Provider value={{ usuario, login: () => {}, logout: () => {} }}>
      <AvisosProvider intervaloMs={intervaloMs}>
        <Avisos />
      </AvisosProvider>
    </AuthContext.Provider>,
  )
}

function pendiente(id: string, fechaDespacho: string | null, recibida = 0) {
  return {
    ordenCompraId: id,
    obraId: 'o-1',
    fechaDespacho,
    items: [
      { articuloId: 'a', descripcionArticulo: 'Cemento', cantidadEsperada: 10, cantidadRecibida: recibida, unidadMedida: 'bolsa' },
    ],
  }
}

function orden(id: string, cambios: Record<string, unknown> = {}) {
  return {
    id,
    licitacionId: 'l-1',
    cotizacionId: `c-${id}`,
    obraId: 'o-1',
    proveedorId: 'p-1',
    estado: 'Emitida',
    fechaEmision: '2026-11-10T10:00:00Z',
    fueDespachada: false,
    ...cambios,
  }
}

describe('AvisosProvider (#219)', () => {
  beforeEach(() => localStorage.clear())

  it('consulta cada 30 segundos', () => {
    expect(INTERVALO_REFRESCO_MS).toBe(30_000)
  })

  it('Capataz: lo que ya estaba despachado no avisa la primera vez; un despacho nuevo sí, y se puede marcar como visto', async () => {
    const user = userEvent.setup()
    let lista = [pendiente(ORDEN_1, '2026-11-20T10:00:00Z')]
    server.use(http.get('*/api/recepciones/ordenes-pendientes', () => HttpResponse.json(lista)))
    renderizar('Capataz', 50)

    await waitFor(() => expect(leerVistos(claveVistosFlujo('capataz', 'u-1'))).not.toBeNull())
    expect(screen.getByTestId('cantidad')).toHaveTextContent('0')

    lista = [...lista, pendiente(ORDEN_2, '2026-11-22T10:00:00Z')]
    await waitFor(() => expect(screen.getByTestId('cantidad')).toHaveTextContent('1'))
    expect(screen.getByTestId('novedades')).toHaveTextContent(`orden-despachada:${ORDEN_2}`)

    await user.click(screen.getByRole('button', { name: 'Marcar todas' }))
    expect(screen.getByTestId('cantidad')).toHaveTextContent('0')
    expect(leerVistos(claveVistosFlujo('capataz', 'u-1'))).toMatchObject({
      [`orden-despachada:${ORDEN_2}`]: '2026-11-22T10:00:00Z',
    })

    // Aunque se vuelva a consultar, no se repite.
    await new Promise((r) => setTimeout(r, 200))
    expect(screen.getByTestId('cantidad')).toHaveTextContent('0')
  })

  it('Capataz: una recepción parcial aparece en "Para hacer" y se va sola cuando se completa', async () => {
    let lista = [pendiente(ORDEN_1, '2026-11-20T10:00:00Z', 4)]
    server.use(http.get('*/api/recepciones/ordenes-pendientes', () => HttpResponse.json(lista)))
    renderizar('Capataz', 50)

    await waitFor(() => expect(screen.getByTestId('para-hacer')).toHaveTextContent(`recepcion-parcial:${ORDEN_1}`))

    lista = []
    await waitFor(() => expect(screen.getByTestId('cantidad')).toHaveTextContent('0'))
  })

  it('Proveedor: al refrescar, el aviso de una orden recién despachada desaparece sin esperar al intervalo', async () => {
    const user = userEvent.setup()
    let despachada = false
    server.use(
      http.get('*/api/ordenes-compra', () =>
        HttpResponse.json([orden(ORDEN_1, { fueDespachada: despachada, fechaDespacho: despachada ? '2026-11-24T10:00:00Z' : null })]),
      ),
      http.get('*/api/cotizaciones/mias', () => HttpResponse.json([])),
    )
    renderizar('Proveedor')
    await waitFor(() => expect(screen.getByTestId('cantidad')).toHaveTextContent('1'))

    despachada = true
    await user.click(screen.getByRole('button', { name: 'Refrescar ahora' }))

    await waitFor(() => expect(screen.getByTestId('para-hacer')).toHaveTextContent(''))
    expect(screen.getByTestId('para-hacer')).not.toHaveTextContent('orden-por-despachar')
  })

  it('Proveedor: la orden por despachar no se pierde ni se duplica al recargar', async () => {
    server.use(
      http.get('*/api/ordenes-compra', () => HttpResponse.json([orden(ORDEN_1)])),
      http.get('*/api/cotizaciones/mias', () => HttpResponse.json([])),
    )
    const primera = renderizar('Proveedor')
    await waitFor(() => expect(screen.getByTestId('cantidad')).toHaveTextContent('1'))
    expect(screen.getByTestId('para-hacer')).toHaveTextContent(`orden-por-despachar:${ORDEN_1}`)
    primera.unmount()

    // "Recargar la página": otro montaje con el mismo navegador.
    renderizar('Proveedor')
    await waitFor(() => expect(screen.getByTestId('cantidad')).toHaveTextContent('1'))
    expect(screen.getByTestId('para-hacer')).toHaveTextContent(`orden-por-despachar:${ORDEN_1}`)
  })

  it('Proveedor: una novedad vista (recepción completa) no vuelve a aparecer después de recargar', async () => {
    const user = userEvent.setup()
    let ordenes = [orden(ORDEN_1, { fueDespachada: true, fechaDespacho: '2026-11-15T10:00:00Z' })]
    server.use(
      http.get('*/api/ordenes-compra', () => HttpResponse.json(ordenes)),
      http.get('*/api/cotizaciones/mias', () => HttpResponse.json([])),
    )
    const primera = renderizar('Proveedor', 50)
    await waitFor(() => expect(leerVistos(claveVistosFlujo('proveedor', 'u-1'))).not.toBeNull())

    ordenes = [
      orden(ORDEN_1, {
        fueDespachada: true,
        fechaDespacho: '2026-11-15T10:00:00Z',
        recibidaCompleta: true,
        fechaRecepcionCompleta: '2026-11-21T10:00:00Z',
      }),
    ]
    await waitFor(() => expect(screen.getByTestId('novedades')).toHaveTextContent(`recepcion-completa:${ORDEN_1}`))

    await user.click(screen.getByRole('button', { name: 'Marcar todas' }))
    expect(screen.getByTestId('cantidad')).toHaveTextContent('0')
    primera.unmount()

    renderizar('Proveedor', 50)
    await new Promise((r) => setTimeout(r, 200))
    expect(screen.getByTestId('cantidad')).toHaveTextContent('0')
  })

  it('Compras: una requisición aprobada sin licitación aparece en "Para hacer"', async () => {
    server.use(
      http.get('*/api/requisiciones', () =>
        HttpResponse.json([
          {
            requisicionId: 'r-1',
            numeroRequisicion: 'REQ-0001',
            obraId: 'o-1',
            solicitanteId: 'u-9',
            estado: 'Aprobada',
            fechaCreacion: '2026-11-01T10:00:00Z',
            items: [],
          },
        ]),
      ),
      http.get('*/api/licitaciones', () => HttpResponse.json([])),
      http.get('*/api/ordenes-compra', () => HttpResponse.json([])),
    )
    renderizar('Comprador')

    await waitFor(() => expect(screen.getByTestId('para-hacer')).toHaveTextContent('requisicion-por-licitar:r-1'))
    expect(screen.getByTestId('cantidad')).toHaveTextContent('1')
  })

  it('Compras: cada lista falla por separado y no rompe las demás', async () => {
    server.use(
      http.get('*/api/requisiciones', () => HttpResponse.error()),
      http.get('*/api/licitaciones', () => HttpResponse.json([])),
      http.get('*/api/ordenes-compra', () =>
        HttpResponse.json([orden(ORDEN_1, { mensajesNoLeidos: 2, fueDespachada: true, fechaDespacho: '2026-11-15T10:00:00Z' })]),
      ),
    )
    renderizar('Admin')

    await waitFor(() => expect(screen.getByTestId('para-hacer')).toHaveTextContent(`orden-chat:${ORDEN_1}`))
    // Con una lista caída no se pudo saber qué era viejo: las novedades esperan, no se inventan.
    expect(screen.getByTestId('novedades')).toBeEmptyDOMElement()
  })

  it('con error de red no rompe y no inventa avisos', async () => {
    server.use(
      http.get('*/api/ordenes-compra', () => HttpResponse.error()),
      http.get('*/api/cotizaciones/mias', () => HttpResponse.error()),
      http.get('*/api/cotizaciones/proveedores/mi-reputacion', () => HttpResponse.error()),
    )
    renderizar('Proveedor')

    await new Promise((r) => setTimeout(r, 100))
    expect(screen.getByTestId('cantidad')).toHaveTextContent('0')
  })

  it('un rol sin avisos del flujo no consulta nada', async () => {
    let pedidos = 0
    server.use(
      http.get('*/api/ordenes-compra', () => {
        pedidos++
        return HttpResponse.json([])
      }),
    )
    renderizar('Otro')

    await new Promise((r) => setTimeout(r, 100))
    expect(pedidos).toBe(0)
    expect(screen.getByTestId('cantidad')).toHaveTextContent('0')
  })
})
