import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { reiniciarOrdenesCompraMock } from '../mocks/handlers'
import { server } from '../mocks/server'
import { OrdenesCompraPage } from './OrdenesCompraPage'

// Estas pruebas cubren el listado y la navegación; el chat de órdenes tiene su propia suite.
vi.mock('../chat/ChatOrden', () => ({ ChatOrden: () => null }))
vi.mock('../chat/useChatEnLista', () => ({
  useChatEnLista: () => ({
    problemaDe: () => ({ abierto: false, motivo: null }),
    noLeidosDe: () => 0,
    marcarLeida: () => {},
    cambiarProblema: () => {},
  }),
}))

const RUTA_LISTA = '*/api/ordenes-compra'

function renderPagina(initialEntry = '/compras/ordenes') {
  return render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <OrdenesCompraPage />
    </MemoryRouter>,
  )
}

/** Las tarjetas de orden, en el orden en que se ven. */
async function tarjetas() {
  await screen.findByRole('list')
  return screen.getAllByRole('listitem')
}

describe('OrdenesCompraPage (#205, #223)', () => {
  beforeEach(() => reiniciarOrdenesCompraMock())

  it('no habilita calificar ninguna orden hasta que el servicio confirme la recepción completa', async () => {
    renderPagina()
    await tarjetas()

    expect(screen.queryByRole('button', { name: 'Guardar calificación' })).not.toBeInTheDocument()
    expect(screen.getAllByText('Vas a poder calificar cuando llegue todo el pedido.')).toHaveLength(3)
  })

  it('lista las órdenes con la obra y su estado: primero las que faltan despachar', async () => {
    renderPagina()

    const filas = await tarjetas()
    expect(filas).toHaveLength(3)
    // Las dos sin despachar (la más nueva primero) y al final la ya despachada.
    expect(within(filas[0]).getByText('Sin despachar')).toBeInTheDocument()
    expect(within(filas[0]).getByText('Emitida el 18 de noviembre de 2026')).toBeInTheDocument()
    expect(within(filas[1]).getByText('Emitida el 17 de noviembre de 2026')).toBeInTheDocument()
    expect(within(filas[2]).getByText('Despachada')).toBeInTheDocument()
    expect(within(filas[0]).getByText('Torre Alvear')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: '← Volver al inicio' })).toHaveAttribute('href', '/')
  })

  it('Compras no puede despachar: ninguna orden tiene botón y las pendientes dicen que espera al proveedor (#223)', async () => {
    renderPagina()

    const filas = await tarjetas()
    expect(screen.queryByRole('button', { name: /despachada/i })).not.toBeInTheDocument()
    expect(within(filas[0]).getByText('Esperando que el proveedor la despache.')).toBeInTheDocument()
    expect(within(filas[1]).getByText('Esperando que el proveedor la despache.')).toBeInTheDocument()
    expect(within(filas[2]).queryByText('Esperando que el proveedor la despache.')).not.toBeInTheDocument()
  })

  it('la orden despachada muestra cuándo se despachó, cuándo llega y la observación (#223)', async () => {
    renderPagina()

    const filas = await tarjetas()
    expect(within(filas[2]).getByText('Despachada el 13 de noviembre de 2026 por el proveedor')).toBeInTheDocument()
    expect(within(filas[2]).getByText('Llega el 20 de noviembre de 2026')).toBeInTheDocument()
    expect(within(filas[2]).getByText('Observación: Sale en camión por la mañana')).toBeInTheDocument()
    expect(within(filas[2]).getByRole('link', { name: 'Ir a Recepción' })).toHaveAttribute('href', '/compras/recepcion')
  })

  it('abre y señala la orden indicada por identificador', async () => {
    renderPagina('/compras/ordenes?ordenCompraId=c1c2c3c4-0000-0000-0000-000000000002')
    const filas = await tarjetas()
    expect(within(filas[1]).getByRole('status')).toHaveTextContent('Esta es la orden generada por la adjudicación.')
    expect(filas[1]).toHaveAttribute('id', 'orden-c1c2c3c4-0000-0000-0000-000000000002')
  })

  it('si falla la carga, avisa y "Reintentar" vuelve a pedir la lista', async () => {
    server.use(http.get(RUTA_LISTA, () => HttpResponse.error(), { once: true }))
    const user = userEvent.setup()
    renderPagina()

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'No se pudieron cargar las órdenes de compra. Intentá nuevamente.',
    )
    await user.click(screen.getByRole('button', { name: 'Reintentar' }))

    expect(await tarjetas()).toHaveLength(3)
  })

  it('si todavía no hay órdenes, lo dice', async () => {
    server.use(http.get(RUTA_LISTA, () => HttpResponse.json([])))
    renderPagina()

    expect(await screen.findByText('Todavía no hay órdenes de compra.')).toBeInTheDocument()
    expect(screen.queryByRole('list')).not.toBeInTheDocument()
  })

  it('bloquea órdenes pendientes y con recepción parcial', async () => {
    const ordenes = [
      {
        id: 'orden-pendiente', licitacionId: 'lic-1', cotizacionId: 'cot-1', obraId: 'obra-1', proveedorId: 'prov-1',
        estado: 'Emitida', fechaEmision: '2026-11-18T12:00:00Z', fueDespachada: false, recibidaCompleta: false,
      },
      {
        id: 'orden-parcial', licitacionId: 'lic-2', cotizacionId: 'cot-2', obraId: 'obra-1', proveedorId: 'prov-2',
        estado: 'Despachada', fechaEmision: '2026-11-17T12:00:00Z', fueDespachada: true, recibidaCompleta: false,
      },
    ]
    server.use(http.get(RUTA_LISTA, () => HttpResponse.json(ordenes)))
    renderPagina()

    await tarjetas()
    expect(screen.getAllByText('Vas a poder calificar cuando llegue todo el pedido.')).toHaveLength(2)
    expect(screen.queryByRole('button', { name: /de 5/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Guardar calificación' })).not.toBeInTheDocument()
  })

  it('envía la calificación por orden y la recupera al volver a entrar desde el listado del servicio', async () => {
    const user = userEvent.setup()
    const orden = {
      id: 'orden-recibida', licitacionId: 'lic-3', cotizacionId: 'cot-3', obraId: 'obra-1', proveedorId: 'prov-3',
      estado: 'Despachada', fechaEmision: '2026-11-16T12:00:00Z', fueDespachada: true, recibidaCompleta: true,
      miCalificacion: null as number | null, miComentarioCalificacion: null as string | null,
    }
    let cuerpo: unknown = null
    server.use(
      http.get(RUTA_LISTA, () => HttpResponse.json([orden])),
      http.put('*/api/ordenes-compra/:id/calificacion', async ({ params, request }) => {
        cuerpo = await request.json()
        const entrada = cuerpo as { calificacion: number; comentario?: string | null }
        orden.miCalificacion = entrada.calificacion
        orden.miComentarioCalificacion = entrada.comentario ?? null
        return HttpResponse.json({
          ordenCompraId: params.id,
          proveedorId: orden.proveedorId,
          usuarioId: 'usuario-comprador',
          calificacion: entrada.calificacion,
          comentario: entrada.comentario ?? null,
          fechaActualizacion: '2026-11-22T12:00:00Z',
          promedioProveedor: entrada.calificacion,
          cantidadCalificacionesProveedor: 1,
        })
      }),
    )
    const primeraVista = renderPagina()
    await screen.findByRole('list')
    await user.click(screen.getByRole('button', { name: '5 de 5' }))
    await user.type(screen.getByRole('textbox', { name: 'Comentario opcional, máximo 300 caracteres' }), 'Entrega completa')
    await user.click(screen.getByRole('button', { name: 'Guardar calificación' }))

    expect(await screen.findByRole('status')).toHaveTextContent('Calificaste con 5 de 5.')
    expect(cuerpo).toEqual({ calificacion: 5, comentario: 'Entrega completa' })
    expect(window.localStorage.getItem('obrasync.calificacion.orden.orden-recibida')).toBeNull()
    primeraVista.unmount()

    renderPagina()
    await screen.findByRole('list')
    expect(screen.getByRole('status')).toHaveTextContent('Calificaste con 5 de 5.')
    expect(screen.getByText('Comentario: Entrega completa')).toBeInTheDocument()
  })
})
