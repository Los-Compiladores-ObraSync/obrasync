import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { reiniciarRecepcionesMock } from '../mocks/handlers'
import { server } from '../mocks/server'
import { descargarFotoEvidencia, obtenerOrdenesPendientes, registrarEntregaConProblema } from '../recepciones/recepcionesService'
import { RecepcionPage } from './RecepcionPage'

// Pruebas de "Hubo un problema con la entrega" y de los datos del despacho (#224).
// El envío con fotos y la descarga de la imagen se simulan: ver el comentario de recepcionesService.test.ts.
vi.mock('../recepciones/recepcionesService', async (original) => ({
  ...(await original<typeof import('../recepciones/recepcionesService')>()),
  registrarEntregaConProblema: vi.fn(),
  descargarFotoEvidencia: vi.fn(),
}))

const RUTA_ORDENES = '*/api/recepciones/ordenes-pendientes'
const RUTA_RECEPCIONES = '*/api/recepciones'

function renderPagina() {
  return render(
    <MemoryRouter>
      <RecepcionPage />
    </MemoryRouter>,
  )
}

/** Espera la lista y toca la primera orden. */
async function elegirPrimeraOrden(user: ReturnType<typeof userEvent.setup>) {
  const tarjetas = await screen.findAllByRole('button', { name: /Torre Alvear/ })
  await user.click(tarjetas[0])
}

async function abrirFormularioDelProblema(user: ReturnType<typeof userEvent.setup>) {
  await elegirPrimeraOrden(user)
  await user.click(screen.getByRole('button', { name: 'Hubo un problema con la entrega' }))
}

describe('RecepcionPage: despacho y entrega con problema (#224)', () => {
  beforeEach(() => {
    reiniciarRecepcionesMock()
    URL.createObjectURL = vi.fn(() => 'blob:prueba')
    URL.revokeObjectURL = vi.fn()
    vi.mocked(registrarEntregaConProblema).mockReset()
    vi.mocked(descargarFotoEvidencia).mockReset()
    vi.mocked(descargarFotoEvidencia).mockResolvedValue(new Blob(['x'], { type: 'image/jpeg' }))
  })

  it('en la lista muestra para cuándo llega cada orden y la observación del Proveedor', async () => {
    renderPagina()

    expect(await screen.findByText('Llega el 23 de noviembre de 2026')).toBeInTheDocument()
    expect(screen.getByText('Observación del Proveedor: Llega en camión chico, antes de las 10.')).toBeInTheDocument()
  })

  it('marca en la lista la orden que tiene un problema de entrega abierto', async () => {
    const ordenes = await obtenerOrdenesPendientes()
    server.use(http.get(RUTA_ORDENES, () => HttpResponse.json(ordenes.map((o, i) => (i === 0 ? { ...o, tieneProblemaAbierto: true } : o)))))
    renderPagina()

    expect(await screen.findByText('Problema de entrega abierto')).toBeInTheDocument()
  })

  it('al elegir una orden muestra la línea de tiempo con el despacho del Proveedor', async () => {
    const user = userEvent.setup()
    renderPagina()

    await elegirPrimeraOrden(user)

    const linea = await screen.findByRole('list', { name: 'Línea de tiempo de la orden' })
    expect(within(linea).getByText('Despachada por el Proveedor')).toBeInTheDocument()
    expect(within(linea).getByText(/Llega el 23 de noviembre de 2026\./)).toBeInTheDocument()
  })

  it('avisa en la orden elegida si tiene un problema abierto', async () => {
    const user = userEvent.setup()
    const ordenes = await obtenerOrdenesPendientes()
    server.use(http.get(RUTA_ORDENES, () => HttpResponse.json(ordenes.map((o, i) => (i === 0 ? { ...o, tieneProblemaAbierto: true } : o)))))
    renderPagina()

    await elegirPrimeraOrden(user)

    expect(screen.getByText(/Esta orden tiene un problema de entrega abierto/)).toBeInTheDocument()
  })

  it('"Hubo un problema con la entrega" abre el formulario y "Volver a la recepción" regresa', async () => {
    const user = userEvent.setup()
    renderPagina()

    await abrirFormularioDelProblema(user)

    expect(screen.getByRole('heading', { name: 'Hubo un problema con la entrega' })).toBeInTheDocument()
    expect(screen.getByText('Obra: Torre Alvear')).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: 'Llegó dañada' })).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: '← Volver a la recepción' }))

    expect(screen.getByRole('button', { name: 'Revisar recepción' })).toBeInTheDocument()
  })

  it('registra el problema y confirma que la orden sigue esperando la mercadería', async () => {
    const user = userEvent.setup()
    vi.mocked(registrarEntregaConProblema).mockResolvedValue({ id: 'p1', estado: 4, motivoProblema: 'Llegó dañada', fotos: [{ id: 'f1' }, { id: 'f2' }] })
    renderPagina()

    await abrirFormularioDelProblema(user)
    await user.click(screen.getByRole('radio', { name: 'Llegó dañada' }))
    await user.click(screen.getByRole('button', { name: 'Revisar reporte' }))
    await user.click(screen.getByRole('button', { name: 'Confirmar reporte' }))

    expect(await screen.findByRole('heading', { name: 'Problema registrado' })).toBeInTheDocument()
    expect(screen.getByText(/La orden sigue esperando la mercadería/)).toBeInTheDocument()
    expect(screen.getByText('Llegó dañada')).toBeInTheDocument()
    expect(screen.getByText('Fotos enviadas:').parentElement).toHaveTextContent('Fotos enviadas: 2')
    expect(registrarEntregaConProblema).toHaveBeenCalledTimes(1)
    expect(vi.mocked(registrarEntregaConProblema).mock.calls[0][0]).toMatchObject({ motivo: 'Llegó dañada' })
  })

  it('después de registrar el problema se puede volver a la lista de órdenes', async () => {
    const user = userEvent.setup()
    vi.mocked(registrarEntregaConProblema).mockResolvedValue({ id: 'p1', estado: 4, motivoProblema: 'No llegó nada', fotos: [] })
    renderPagina()

    await abrirFormularioDelProblema(user)
    await user.click(screen.getByRole('radio', { name: 'No llegó nada' }))
    await user.click(screen.getByRole('button', { name: 'Revisar reporte' }))
    await user.click(screen.getByRole('button', { name: 'Confirmar reporte' }))
    await user.click(await screen.findByRole('button', { name: 'Volver a las órdenes' }))

    expect(await screen.findAllByRole('button', { name: /Torre Alvear/ })).toHaveLength(2)
  })

  it('si registrar falla, el formulario sigue ahí con el error', async () => {
    const user = userEvent.setup()
    vi.mocked(registrarEntregaConProblema).mockRejectedValue(new Error('sin señal'))
    renderPagina()

    await abrirFormularioDelProblema(user)
    await user.click(screen.getByRole('radio', { name: 'No llegó nada' }))
    await user.click(screen.getByRole('button', { name: 'Revisar reporte' }))
    await user.click(screen.getByRole('button', { name: 'Confirmar reporte' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudo conectar con el servidor')
    expect(screen.getByRole('button', { name: 'Revisar reporte' })).toBeEnabled()
  })

  it('una entrega con problema ya registrada aparece en la línea de tiempo con su motivo y sus fotos', async () => {
    const user = userEvent.setup()
    server.use(
      http.get(RUTA_RECEPCIONES, () =>
        HttpResponse.json([
          {
            id: 'p1',
            numeroRemito: 'PROBLEMA-1a2b3c4d',
            fechaRecepcion: '2026-11-22T14:00:00Z',
            createdAt: '2026-11-22T14:00:00Z',
            estado: 4,
            motivoProblema: 'Llegó incompleta',
            observaciones: 'faltaron 10 bolsas',
            fotos: [{ id: 'f1' }],
          },
        ]),
      ),
    )
    renderPagina()

    await elegirPrimeraOrden(user)

    expect(await screen.findByText('Entrega con problema')).toBeInTheDocument()
    expect(screen.getByText('Motivo: Llegó incompleta')).toBeInTheDocument()
    expect(screen.getByText('Detalle: faltaron 10 bolsas')).toBeInTheDocument()
    expect(await screen.findByAltText('Foto 1 de la entrega con problema')).toBeInTheDocument()
    expect(descargarFotoEvidencia).toHaveBeenCalledWith('p1', 'f1')
  })
})
