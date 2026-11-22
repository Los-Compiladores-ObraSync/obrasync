import { render, screen, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LineaDeTiempoOrden } from './LineaDeTiempoOrden'
import { descargarFotoEvidencia } from './recepcionesService'
import type { RecepcionHistorial } from './recepcionesTipos'

vi.mock('./recepcionesService', () => ({ descargarFotoEvidencia: vi.fn() }))

const DESPACHO = {
  fechaDespacho: '2026-11-20T15:00:00Z',
  fechaEstimadaLlegada: '2026-11-23T12:00:00Z',
  observacionDespacho: 'Llega en camión chico, antes de las 10.',
}

function parcial(): RecepcionHistorial {
  return {
    id: 'r1',
    numeroRemito: 'R-001',
    fechaRecepcion: '2026-11-23T14:00:00Z',
    createdAt: '2026-11-23T14:00:00Z',
    estado: 2,
    detalles: [
      { id: 'd1', descripcionArticulo: 'Cemento Portland Especial 25kg', cantidadRecibida: 75, unidadMedida: 'BOLSA' },
      { id: 'd2', descripcionArticulo: 'Arena Fina a Granel', cantidadRecibida: 12, unidadMedida: 'M3', conforme: false, observacion: 'húmeda' },
    ],
  }
}

function problema(): RecepcionHistorial {
  return {
    id: 'p1',
    numeroRemito: 'PROBLEMA-1a2b3c4d',
    fechaRecepcion: '2026-11-23T10:00:00Z',
    createdAt: '2026-11-23T10:00:00Z',
    estado: 4,
    motivoProblema: 'Llegó dañada',
    observaciones: '3 bolsas rotas',
    fotos: [{ id: 'f1' }, { id: 'f2' }],
  }
}

function completa(): RecepcionHistorial {
  return { id: 'r2', numeroRemito: 'R-002', fechaRecepcion: '2026-11-24T14:00:00Z', createdAt: '2026-11-24T14:00:00Z', estado: 3, detalles: [] }
}

beforeEach(() => {
  URL.createObjectURL = vi.fn(() => 'blob:prueba')
  URL.revokeObjectURL = vi.fn()
  vi.mocked(descargarFotoEvidencia).mockResolvedValue(new Blob(['x'], { type: 'image/jpeg' }))
})

function linea() {
  return screen.getByRole('list', { name: 'Línea de tiempo de la orden' })
}

describe('LineaDeTiempoOrden', () => {
  it('muestra el despacho del Proveedor con la fecha estimada y su observación', () => {
    render(<LineaDeTiempoOrden despacho={DESPACHO} recepciones={[]} />)

    expect(within(linea()).getByText('Despachada por el Proveedor')).toBeInTheDocument()
    expect(linea()).toHaveTextContent(/20 de noviembre de 2026/)
    expect(screen.getByText(/Llega el 23 de noviembre de 2026\./)).toBeInTheDocument()
    expect(screen.getByText('Observación del Proveedor: Llega en camión chico, antes de las 10.')).toBeInTheDocument()
  })

  it('sin fecha estimada ni observación solo dice cuándo despachó', () => {
    render(<LineaDeTiempoOrden despacho={{ fechaDespacho: '2026-11-20T15:00:00Z' }} recepciones={[]} />)

    expect(screen.queryByText(/Llega el/)).not.toBeInTheDocument()
    expect(screen.queryByText(/Observación del Proveedor/)).not.toBeInTheDocument()
  })

  it('muestra una recepción parcial con el remito, el estado y lo recibido de cada material', () => {
    render(<LineaDeTiempoOrden recepciones={[parcial()]} />)

    expect(screen.getByText('Remito R-001')).toBeInTheDocument()
    expect(linea()).toHaveTextContent('Parcial')
    expect(screen.getByText(/Cemento Portland Especial 25kg: 75 bolsas/)).toBeInTheDocument()
    expect(screen.getByText(/Con problemas: húmeda/)).toBeInTheDocument()
  })

  it('muestra una entrega con problema con su motivo, su detalle y sus fotos, sin el remito interno', async () => {
    render(<LineaDeTiempoOrden recepciones={[problema()]} />)

    expect(screen.getByText('Entrega con problema')).toBeInTheDocument()
    expect(screen.getByText('Motivo: Llegó dañada')).toBeInTheDocument()
    expect(screen.getByText('Detalle: 3 bolsas rotas')).toBeInTheDocument()
    expect(screen.queryByText(/PROBLEMA-/)).not.toBeInTheDocument()
    expect(await screen.findByAltText('Foto 1 de la entrega con problema')).toBeInTheDocument()
    expect(await screen.findByAltText('Foto 2 de la entrega con problema')).toBeInTheDocument()
    expect(descargarFotoEvidencia).toHaveBeenCalledWith('p1', 'f1')
    expect(descargarFotoEvidencia).toHaveBeenCalledWith('p1', 'f2')
  })

  it('una entrega con problema sin fotos no muestra ninguna', () => {
    render(<LineaDeTiempoOrden recepciones={[{ ...problema(), fotos: [] }]} />)

    expect(screen.queryByRole('button', { name: /Ampliar foto/ })).not.toBeInTheDocument()
    expect(descargarFotoEvidencia).not.toHaveBeenCalled()
  })

  it('ordena todo del hecho más viejo al más nuevo, aunque lleguen desordenados', () => {
    render(<LineaDeTiempoOrden despacho={DESPACHO} recepciones={[completa(), parcial(), problema()]} />)

    const texto = linea().textContent ?? ''
    const posiciones = ['Despachada por el Proveedor', 'Entrega con problema', 'Remito R-001', 'Remito R-002'].map((t) => texto.indexOf(t))
    expect(posiciones.every((p) => p >= 0)).toBe(true)
    expect([...posiciones].sort((a, b) => a - b)).toEqual(posiciones)
    expect(linea()).toHaveTextContent('Completa')
  })

  it('no dibuja nada si no hay despacho ni recepciones', () => {
    const { container } = render(<LineaDeTiempoOrden despacho={{}} recepciones={[]} />)

    expect(container).toBeEmptyDOMElement()
  })
})
