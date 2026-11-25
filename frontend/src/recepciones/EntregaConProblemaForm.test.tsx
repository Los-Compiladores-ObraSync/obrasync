import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../api/ApiError'
import { EntregaConProblemaForm } from './EntregaConProblemaForm'
import { reducirFotoEvidencia } from './imagenEvidencia'
import { ErrorDeImagen } from './imagenRemito'
import { registrarEntregaConProblema } from './recepcionesService'
import type { OrdenPendiente } from './recepcionesTipos'

// El achicado de fotos usa APIs del navegador que jsdom no tiene, y el envío real no se prueba acá: se simulan.
vi.mock('./imagenEvidencia', async (original) => ({
  ...(await original<typeof import('./imagenEvidencia')>()),
  reducirFotoEvidencia: vi.fn(),
}))
vi.mock('./recepcionesService', async (original) => ({
  ...(await original<typeof import('./recepcionesService')>()),
  registrarEntregaConProblema: vi.fn(),
}))

const ORDEN: OrdenPendiente = { ordenCompraId: 'orden-1', obraId: 'obra-1', items: [] }
const REGISTRADA = { id: 'p1', estado: 4, motivoProblema: 'Llegó dañada' as const, fotos: [] }

const foto = (nombre = 'foto.jpg') => new File(['x'], nombre, { type: 'image/jpeg' })

const ORDEN_CON_ITEMS: OrdenPendiente = {
  ...ORDEN,
  items: [
    { articuloId: 'a1', descripcionArticulo: 'Cemento', cantidadEsperada: 20, cantidadRecibida: 0, unidadMedida: 'bolsa' },
    { articuloId: 'a2', descripcionArticulo: 'Hierro del 8', cantidadEsperada: 10, cantidadRecibida: 0, unidadMedida: 'barra' },
  ],
}

function renderFormulario(orden: OrdenPendiente = ORDEN) {
  const onRegistrada = vi.fn()
  const onCancelar = vi.fn()
  render(<EntregaConProblemaForm orden={orden} onRegistrada={onRegistrada} onCancelar={onCancelar} />)
  return { onRegistrada, onCancelar }
}

let contador = 0

beforeEach(() => {
  URL.createObjectURL = vi.fn(() => `blob:vista-previa-${(contador += 1)}`)
  URL.revokeObjectURL = vi.fn()
  // Cada foto elegida sale "achicada" como un Blob distinto, para poder reconocerlo al enviar.
  vi.mocked(reducirFotoEvidencia).mockImplementation(async () => new Blob(['achicada'], { type: 'image/jpeg' }))
  vi.mocked(registrarEntregaConProblema).mockResolvedValue(REGISTRADA)
})

afterEach(() => {
  vi.clearAllMocks()
})

describe('EntregaConProblemaForm', () => {
  it('ofrece los cuatro motivos del contrato', () => {
    renderFormulario()

    for (const motivo of ['No llegó nada', 'Llegó incompleta', 'Llegó dañada', 'No es lo pedido']) {
      expect(screen.getByRole('radio', { name: motivo })).not.toBeChecked()
    }
  })

  it('no deja registrar sin elegir el motivo', async () => {
    const user = userEvent.setup()
    renderFormulario()

    await user.click(screen.getByRole('button', { name: 'Registrar el problema' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Elegí qué pasó con la entrega.')
    expect(registrarEntregaConProblema).not.toHaveBeenCalled()
  })

  it('registra el motivo y el detalle, sin fotos', async () => {
    const user = userEvent.setup()
    const { onRegistrada } = renderFormulario()

    await user.click(screen.getByRole('radio', { name: 'Llegó dañada' }))
    await user.type(screen.getByLabelText('Detalle (opcional)'), '  3 bolsas rotas  ')
    await user.click(screen.getByRole('button', { name: 'Registrar el problema' }))

    await waitFor(() => expect(onRegistrada).toHaveBeenCalledWith(REGISTRADA))
    expect(registrarEntregaConProblema).toHaveBeenCalledWith(
      { obraId: 'obra-1', ordenCompraId: 'orden-1', motivo: 'Llegó dañada', detalle: '3 bolsas rotas' },
      [],
    )
  })

  it('un detalle vacío se manda como null', async () => {
    const user = userEvent.setup()
    renderFormulario()

    await user.click(screen.getByRole('radio', { name: 'No llegó nada' }))
    await user.click(screen.getByRole('button', { name: 'Registrar el problema' }))

    await waitFor(() => expect(registrarEntregaConProblema).toHaveBeenCalled())
    expect(vi.mocked(registrarEntregaConProblema).mock.calls[0][0]).toEqual({
      obraId: 'obra-1',
      ordenCompraId: 'orden-1',
      motivo: 'No llegó nada',
      detalle: null,
    })
  })

  it('cuenta los caracteres del detalle y corta en 500', async () => {
    const user = userEvent.setup()
    renderFormulario()

    await user.type(screen.getByLabelText('Detalle (opcional)'), 'hola')

    expect(screen.getByText(/4\/500/)).toBeInTheDocument()
    expect(screen.getByLabelText('Detalle (opcional)')).toHaveAttribute('maxlength', '500')
  })

  it('agrega fotos achicadas con su vista previa y deja quitarlas', async () => {
    const user = userEvent.setup()
    renderFormulario()

    await user.upload(screen.getByLabelText('Elegir fotos de la galería'), [foto('a.jpg'), foto('b.jpg')])

    expect(await screen.findByAltText('Foto 1 para enviar')).toBeInTheDocument()
    expect(screen.getByAltText('Foto 2 para enviar')).toBeInTheDocument()
    expect(reducirFotoEvidencia).toHaveBeenCalledTimes(2)

    await user.click(screen.getByRole('button', { name: 'Quitar foto 1' }))

    // Quedó solo la segunda (ahora es la "Foto 1"), y se liberó únicamente la vista previa de la que se quitó.
    expect(screen.getAllByRole('img')).toHaveLength(1)
    expect(screen.queryByAltText('Foto 2 para enviar')).not.toBeInTheDocument()
    expect(URL.revokeObjectURL).toHaveBeenCalledTimes(1)
  })

  it('dos fotos elegidas juntas tienen vistas previas distintas, y quitar una no toca la otra', async () => {
    const user = userEvent.setup()
    renderFormulario()

    await user.upload(screen.getByLabelText('Elegir fotos de la galería'), [foto('a.jpg'), foto('b.jpg')])
    const [primera, segunda] = (await screen.findAllByRole('img')).map((img) => img.getAttribute('src'))
    expect(primera).not.toBe(segunda)

    await user.click(screen.getByRole('button', { name: 'Quitar foto 2' }))

    expect(screen.getAllByRole('img').map((img) => img.getAttribute('src'))).toEqual([primera])
    expect(URL.revokeObjectURL).toHaveBeenCalledWith(segunda)
  })

  it('manda las fotos ya achicadas, no las originales', async () => {
    const user = userEvent.setup()
    const achicadas = [new Blob(['uno']), new Blob(['dos'])]
    vi.mocked(reducirFotoEvidencia).mockResolvedValueOnce(achicadas[0]).mockResolvedValueOnce(achicadas[1])
    renderFormulario()

    await user.click(screen.getByRole('radio', { name: 'Llegó dañada' }))
    await user.upload(screen.getByLabelText('Elegir fotos de la galería'), [foto('a.jpg'), foto('b.jpg')])
    await screen.findByAltText('Foto 2 para enviar')
    await user.click(screen.getByRole('button', { name: 'Registrar el problema' }))

    await waitFor(() => expect(registrarEntregaConProblema).toHaveBeenCalled())
    expect(vi.mocked(registrarEntregaConProblema).mock.calls[0][1]).toEqual(achicadas)
  })

  it('también deja sacar la foto con la cámara', async () => {
    const user = userEvent.setup()
    renderFormulario()

    await user.upload(screen.getByLabelText('Sacar foto'), foto())

    expect(await screen.findByAltText('Foto 1 para enviar')).toBeInTheDocument()
  })

  it('acepta hasta 3 fotos: si se eligen más, agrega las primeras 3 y avisa', async () => {
    const user = userEvent.setup()
    renderFormulario()

    await user.upload(screen.getByLabelText('Elegir fotos de la galería'), [foto('1.jpg'), foto('2.jpg'), foto('3.jpg'), foto('4.jpg')])

    expect(await screen.findByAltText('Foto 3 para enviar')).toBeInTheDocument()
    expect(screen.queryByAltText('Foto 4 para enviar')).not.toBeInTheDocument()
    expect(reducirFotoEvidencia).toHaveBeenCalledTimes(3)
    expect(screen.getByRole('alert')).toHaveTextContent('Solo entran 3 fotos: agregué las primeras 3.')
    expect(screen.getByRole('button', { name: 'Sacar foto' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Elegir de la galería' })).toBeDisabled()
  })

  it('muestra el texto del error si una foto no se puede preparar, y no la agrega', async () => {
    const user = userEvent.setup()
    vi.mocked(reducirFotoEvidencia).mockRejectedValueOnce(new ErrorDeImagen('No pude abrir esa foto. Probá con otra.'))
    renderFormulario()

    await user.upload(screen.getByLabelText('Sacar foto'), foto())

    expect(await screen.findByRole('alert')).toHaveTextContent('No pude abrir esa foto. Probá con otra.')
    expect(screen.queryByAltText('Foto 1 para enviar')).not.toBeInTheDocument()
  })

  it('mientras prepara una foto no deja enviar', async () => {
    const user = userEvent.setup()
    let terminar: (imagen: Blob) => void = () => undefined
    vi.mocked(reducirFotoEvidencia).mockImplementationOnce(() => new Promise<Blob>((resolver) => { terminar = resolver }))
    renderFormulario()

    await user.upload(screen.getByLabelText('Sacar foto'), foto())

    expect(await screen.findByRole('status')).toHaveTextContent('Preparando la foto…')
    expect(screen.getByRole('button', { name: 'Registrar el problema' })).toBeDisabled()

    terminar(new Blob(['x']))
    expect(await screen.findByAltText('Foto 1 para enviar')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Registrar el problema' })).toBeEnabled()
  })

  it('si el servidor rechaza (409) muestra el motivo y conserva las fotos para reintentar', async () => {
    const user = userEvent.setup()
    vi.mocked(registrarEntregaConProblema).mockRejectedValueOnce(new ApiError(409, { title: 'Conflicto', detail: 'La orden de compra está bloqueada.' }))
    const { onRegistrada } = renderFormulario()

    await user.click(screen.getByRole('radio', { name: 'Llegó dañada' }))
    await user.upload(screen.getByLabelText('Sacar foto'), foto())
    await screen.findByAltText('Foto 1 para enviar')
    await user.click(screen.getByRole('button', { name: 'Registrar el problema' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('La orden de compra está bloqueada.')
    expect(onRegistrada).not.toHaveBeenCalled()
    expect(screen.getByAltText('Foto 1 para enviar')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Registrar el problema' })).toBeEnabled()
  })

  it('si no hay conexión avisa que las fotos siguen ahí', async () => {
    const user = userEvent.setup()
    vi.mocked(registrarEntregaConProblema).mockRejectedValueOnce(new ApiError(0, { title: 'Sin conexión', detail: 'No pudimos conectarnos, reintentá.' }))
    renderFormulario()

    await user.click(screen.getByRole('radio', { name: 'No llegó nada' }))
    await user.click(screen.getByRole('button', { name: 'Registrar el problema' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Tus fotos siguen acá')
  })

  it('un doble toque manda un solo pedido', async () => {
    const user = userEvent.setup()
    let terminar: () => void = () => undefined
    vi.mocked(registrarEntregaConProblema).mockImplementationOnce(() => new Promise((resolver) => { terminar = () => resolver(REGISTRADA) }))
    const { onRegistrada } = renderFormulario()

    await user.click(screen.getByRole('radio', { name: 'No es lo pedido' }))
    const boton = screen.getByRole('button', { name: 'Registrar el problema' })
    await user.click(boton)
    await user.click(screen.getByRole('button', { name: 'Guardando…' }))

    expect(registrarEntregaConProblema).toHaveBeenCalledTimes(1)
    terminar()
    await waitFor(() => expect(onRegistrada).toHaveBeenCalledTimes(1))
  })

  it('"Volver" cancela sin registrar nada', async () => {
    const user = userEvent.setup()
    const { onCancelar } = renderFormulario()

    await user.click(screen.getByRole('button', { name: 'Volver' }))

    expect(onCancelar).toHaveBeenCalledTimes(1)
    expect(registrarEntregaConProblema).not.toHaveBeenCalled()
  })

  describe('"Llegó incompleta"', () => {
    it('solo con ese motivo pregunta si todo o algo específico', async () => {
      const user = userEvent.setup()
      renderFormulario(ORDEN_CON_ITEMS)
      expect(screen.queryByText('¿Todo llegó incompleto o algo específico?')).not.toBeInTheDocument()

      await user.click(screen.getByRole('radio', { name: 'Llegó dañada' }))
      expect(screen.queryByText('¿Todo llegó incompleto o algo específico?')).not.toBeInTheDocument()

      await user.click(screen.getByRole('radio', { name: 'Llegó incompleta' }))
      expect(screen.getByText('¿Todo llegó incompleto o algo específico?')).toBeInTheDocument()
    })

    it('exige elegir y, si es algo específico, marcar al menos un material', async () => {
      const user = userEvent.setup()
      renderFormulario(ORDEN_CON_ITEMS)
      await user.click(screen.getByRole('radio', { name: 'Llegó incompleta' }))

      await user.click(screen.getByRole('button', { name: 'Registrar el problema' }))
      expect(await screen.findByText('Contanos si llegó incompleto todo o solo algo.')).toBeInTheDocument()

      await user.click(screen.getByRole('radio', { name: 'Algo específico' }))
      await user.click(screen.getByRole('button', { name: 'Registrar el problema' }))
      expect(await screen.findByText('Marcá qué materiales llegaron incompletos.')).toBeInTheDocument()
      expect(registrarEntregaConProblema).not.toHaveBeenCalled()
    })

    it('manda qué materiales llegaron incompletos al principio del detalle', async () => {
      const user = userEvent.setup()
      renderFormulario(ORDEN_CON_ITEMS)
      await user.click(screen.getByRole('radio', { name: 'Llegó incompleta' }))
      await user.click(screen.getByRole('radio', { name: 'Algo específico' }))
      await user.click(screen.getByRole('checkbox', { name: 'Hierro del 8' }))
      await user.type(screen.getByLabelText(/Detalle/), 'faltaron 3 barras')
      await user.click(screen.getByRole('button', { name: 'Registrar el problema' }))

      await waitFor(() => expect(registrarEntregaConProblema).toHaveBeenCalled())
      expect(vi.mocked(registrarEntregaConProblema).mock.calls[0][0]).toMatchObject({
        motivo: 'Llegó incompleta',
        detalle: 'Llegó incompleto: Hierro del 8. faltaron 3 barras',
      })
    })

    it('si todo llegó incompleto lo aclara en el detalle', async () => {
      const user = userEvent.setup()
      renderFormulario(ORDEN_CON_ITEMS)
      await user.click(screen.getByRole('radio', { name: 'Llegó incompleta' }))
      await user.click(screen.getByRole('radio', { name: 'Todo llegó incompleto' }))
      await user.click(screen.getByRole('button', { name: 'Registrar el problema' }))

      await waitFor(() => expect(registrarEntregaConProblema).toHaveBeenCalled())
      expect(vi.mocked(registrarEntregaConProblema).mock.calls[0][0].detalle).toBe('Llegó incompleto todo el pedido.')
    })
  })
})
