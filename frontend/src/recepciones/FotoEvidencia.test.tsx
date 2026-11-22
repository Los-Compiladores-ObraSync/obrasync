import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { FotoEvidencia } from './FotoEvidencia'
import { descargarFotoEvidencia } from './recepcionesService'

vi.mock('./recepcionesService', () => ({ descargarFotoEvidencia: vi.fn() }))

const imagen = () => new Blob(['x'], { type: 'image/jpeg' })

function renderFoto() {
  return render(<FotoEvidencia recepcionId="r1" foto={{ id: 'f1' }} numero={1} />)
}

beforeEach(() => {
  // jsdom no trae estas funciones del navegador.
  URL.createObjectURL = vi.fn(() => 'blob:prueba')
  URL.revokeObjectURL = vi.fn()
  vi.mocked(descargarFotoEvidencia).mockResolvedValue(imagen())
})

afterEach(() => {
  vi.clearAllMocks()
})

describe('FotoEvidencia', () => {
  it('baja la foto con el token y la muestra', async () => {
    renderFoto()

    expect(screen.getByRole('status')).toHaveTextContent('Cargando foto 1…')
    const foto = await screen.findByAltText('Foto 1 de la entrega con problema')

    expect(foto).toHaveAttribute('src', 'blob:prueba')
    expect(descargarFotoEvidencia).toHaveBeenCalledWith('r1', 'f1')
  })

  it('tocarla la agranda y "Cerrar foto" la cierra', async () => {
    const user = userEvent.setup()
    renderFoto()

    await user.click(await screen.findByRole('button', { name: 'Ampliar foto 1' }))
    expect(screen.getByRole('dialog', { name: 'Foto 1 ampliada' })).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Cerrar foto' }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('Escape cierra la foto ampliada', async () => {
    const user = userEvent.setup()
    renderFoto()

    await user.click(await screen.findByRole('button', { name: 'Ampliar foto 1' }))
    await user.keyboard('{Escape}')

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('avisa si no se pudo bajar y deja reintentar', async () => {
    const user = userEvent.setup()
    vi.mocked(descargarFotoEvidencia).mockRejectedValueOnce(new Error('sin señal'))
    renderFoto()

    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudo cargar la foto 1.')

    await user.click(screen.getByRole('button', { name: 'Reintentar foto 1' }))

    expect(await screen.findByAltText('Foto 1 de la entrega con problema')).toBeInTheDocument()
    expect(descargarFotoEvidencia).toHaveBeenCalledTimes(2)
  })

  it('libera la imagen de la memoria al salir de la pantalla', async () => {
    const { unmount } = renderFoto()
    await screen.findByAltText('Foto 1 de la entrega con problema')

    unmount()

    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:prueba')
  })
})
