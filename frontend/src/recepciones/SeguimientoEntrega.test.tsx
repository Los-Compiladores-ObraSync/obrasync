import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../api/ApiError'
import { descargarFotoEvidencia, obtenerRecepcionesDeOrden } from './recepcionesService'
import type { RecepcionesDeOrden } from './recepcionesTipos'
import { SeguimientoEntrega } from './SeguimientoEntrega'

vi.mock('./recepcionesService', () => ({ obtenerRecepcionesDeOrden: vi.fn(), descargarFotoEvidencia: vi.fn() }))

const DATOS: RecepcionesDeOrden = {
  ordenCompraId: 'o1',
  obraId: 'ob1',
  fechaDespacho: '2026-11-20T15:00:00Z',
  fechaEstimadaLlegada: '2026-11-23T12:00:00Z',
  observacionDespacho: 'Llega temprano',
  estaCompleta: false,
  tieneProblemaAbierto: false,
  items: [
    {
      articuloId: 'a1',
      descripcionArticulo: 'Cemento Portland Especial 25kg',
      cantidadEsperada: 100,
      cantidadRecibida: 75,
      cantidadPendiente: 25,
      unidadMedida: 'BOLSA',
    },
  ],
  recepciones: [
    {
      id: 'r1',
      numeroRemito: 'R-001',
      fechaRecepcion: '2026-11-24T14:00:00Z',
      createdAt: '2026-11-24T14:00:00Z',
      estado: 2,
      detalles: [{ id: 'd1', descripcionArticulo: 'Cemento Portland Especial 25kg', cantidadRecibida: 75, unidadMedida: 'BOLSA' }],
    },
  ],
}

const NOMBRE_VER = 'Ver seguimiento de la entrega de la orden C1C2C3C4'
const NOMBRE_OCULTAR = 'Ocultar seguimiento de la entrega de la orden C1C2C3C4'

function renderSeguimiento() {
  return render(<SeguimientoEntrega ordenCompraId="o1" codigo="C1C2C3C4" />)
}

beforeEach(() => {
  URL.createObjectURL = vi.fn(() => 'blob:prueba')
  URL.revokeObjectURL = vi.fn()
  vi.mocked(obtenerRecepcionesDeOrden).mockResolvedValue(DATOS)
  vi.mocked(descargarFotoEvidencia).mockResolvedValue(new Blob(['x'], { type: 'image/jpeg' }))
})

afterEach(() => {
  vi.clearAllMocks()
})

describe('SeguimientoEntrega', () => {
  it('arranca cerrado y no consulta nada hasta que se abre', () => {
    renderSeguimiento()

    expect(screen.getByRole('button', { name: NOMBRE_VER })).toHaveAttribute('aria-expanded', 'false')
    expect(obtenerRecepcionesDeOrden).not.toHaveBeenCalled()
    expect(screen.queryByRole('list')).not.toBeInTheDocument()
  })

  it('al abrirlo pide la orden y mientras tanto avisa que carga', async () => {
    const user = userEvent.setup()
    let resolver: (datos: RecepcionesDeOrden) => void = () => undefined
    vi.mocked(obtenerRecepcionesDeOrden).mockImplementationOnce(() => new Promise((resolve) => { resolver = resolve }))
    renderSeguimiento()

    await user.click(screen.getByRole('button', { name: NOMBRE_VER }))

    expect(screen.getByRole('status')).toHaveTextContent('Cargando el seguimiento…')
    expect(obtenerRecepcionesDeOrden).toHaveBeenCalledWith('o1')
    resolver(DATOS)
    expect(await screen.findByRole('list', { name: 'Materiales de la orden' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: NOMBRE_OCULTAR })).toHaveAttribute('aria-expanded', 'true')
  })

  it('muestra qué se pidió, qué llegó y qué falta de cada material', async () => {
    const user = userEvent.setup()
    renderSeguimiento()

    await user.click(screen.getByRole('button', { name: NOMBRE_VER }))

    const materiales = await screen.findByRole('list', { name: 'Materiales de la orden' })
    expect(within(materiales).getByText(/Adjudicado: 100 bolsas/)).toHaveTextContent('Recibido: 75 bolsas · Por recibir: 25 bolsas')
  })

  it('muestra la historia con el despacho y la recepción parcial', async () => {
    const user = userEvent.setup()
    renderSeguimiento()

    await user.click(screen.getByRole('button', { name: NOMBRE_VER }))

    const linea = await screen.findByRole('list', { name: 'Línea de tiempo de la orden' })
    expect(within(linea).getByText('Despachada por el Proveedor')).toBeInTheDocument()
    expect(within(linea).getByText(/Llega el 23 de noviembre de 2026\./)).toBeInTheDocument()
    expect(within(linea).getByText('Remito R-001')).toBeInTheDocument()
    expect(linea).toHaveTextContent('Parcial')
  })

  it('con un problema abierto lo avisa y muestra la entrega con problema y sus fotos', async () => {
    const user = userEvent.setup()
    vi.mocked(obtenerRecepcionesDeOrden).mockResolvedValue({
      ...DATOS,
      tieneProblemaAbierto: true,
      recepciones: [
        {
          id: 'p1',
          estado: 4,
          motivoProblema: 'Llegó dañada',
          observaciones: '3 bolsas rotas',
          fechaRecepcion: '2026-11-22T10:00:00Z',
          createdAt: '2026-11-22T10:00:00Z',
          fotos: [{ id: 'f1' }],
        },
      ],
    })
    renderSeguimiento()

    await user.click(screen.getByRole('button', { name: NOMBRE_VER }))

    expect(await screen.findByText('Hay un problema de entrega abierto en esta orden.')).toBeInTheDocument()
    expect(screen.getByText('Entrega con problema')).toBeInTheDocument()
    expect(screen.getByText('Motivo: Llegó dañada')).toBeInTheDocument()
    expect(await screen.findByAltText('Foto 1 de la entrega con problema')).toBeInTheDocument()
    expect(descargarFotoEvidencia).toHaveBeenCalledWith('p1', 'f1')
  })

  it('avisa cuando la orden ya se recibió por completo', async () => {
    const user = userEvent.setup()
    vi.mocked(obtenerRecepcionesDeOrden).mockResolvedValue({ ...DATOS, estaCompleta: true })
    renderSeguimiento()

    await user.click(screen.getByRole('button', { name: NOMBRE_VER }))

    expect(await screen.findByText('Entrega completa.')).toBeInTheDocument()
  })

  it('si todavía no hay movimientos lo dice', async () => {
    const user = userEvent.setup()
    vi.mocked(obtenerRecepcionesDeOrden).mockResolvedValue({ ...DATOS, fechaDespacho: null, items: [], recepciones: [] })
    renderSeguimiento()

    await user.click(screen.getByRole('button', { name: NOMBRE_VER }))

    expect(await screen.findByText('Todavía no hay movimientos en esta orden.')).toBeInTheDocument()
    expect(screen.queryByRole('list')).not.toBeInTheDocument()
  })

  it('si Recepción todavía no conoce la orden (404) explica que hay que esperar, sin marcar error', async () => {
    const user = userEvent.setup()
    vi.mocked(obtenerRecepcionesDeOrden).mockRejectedValue(new ApiError(404, { title: 'Orden inexistente' }))
    renderSeguimiento()

    await user.click(screen.getByRole('button', { name: NOMBRE_VER }))

    expect(await screen.findByText(/Todavía no hay datos de entrega para esta orden/)).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('si falla la consulta avisa y deja reintentar', async () => {
    const user = userEvent.setup()
    vi.mocked(obtenerRecepcionesDeOrden).mockRejectedValueOnce(new Error('sin señal'))
    renderSeguimiento()

    await user.click(screen.getByRole('button', { name: NOMBRE_VER }))
    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudo cargar el seguimiento de la entrega.')

    await user.click(screen.getByRole('button', { name: 'Reintentar' }))

    expect(await screen.findByRole('list', { name: 'Materiales de la orden' })).toBeInTheDocument()
    expect(obtenerRecepcionesDeOrden).toHaveBeenCalledTimes(2)
  })

  it('"Ocultar" lo cierra, y al abrirlo otra vez consulta de nuevo (pudo llegar mercadería)', async () => {
    const user = userEvent.setup()
    renderSeguimiento()

    await user.click(screen.getByRole('button', { name: NOMBRE_VER }))
    await screen.findByRole('list', { name: 'Materiales de la orden' })
    await user.click(screen.getByRole('button', { name: NOMBRE_OCULTAR }))

    expect(screen.queryByRole('list', { name: 'Materiales de la orden' })).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: NOMBRE_VER }))
    expect(await screen.findByRole('list', { name: 'Materiales de la orden' })).toBeInTheDocument()
    expect(obtenerRecepcionesDeOrden).toHaveBeenCalledTimes(2)
  })
})
