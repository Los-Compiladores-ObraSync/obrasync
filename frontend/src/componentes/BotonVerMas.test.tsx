import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { BotonVerMas } from './BotonVerMas'

const base = { cargando: false, error: false, onVerMas: () => {} }

describe('BotonVerMas', () => {
  it('muestra cuántas filas hay de cuántas y el botón', () => {
    render(<BotonVerMas {...base} cargadas={50} total={120} nombre="insumos" />)

    expect(screen.getByText('Mostrando 50 de 120 insumos.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Ver más' })).toBeEnabled()
  })

  it('no se dibuja cuando ya está todo cargado', () => {
    const { container } = render(<BotonVerMas {...base} cargadas={120} total={120} />)

    expect(container).toBeEmptyDOMElement()
  })

  it('no se dibuja si el servicio no informa el total', () => {
    const { container } = render(<BotonVerMas {...base} cargadas={50} total={undefined} />)

    expect(container).toBeEmptyDOMElement()
  })

  it('al tocarlo pide la página siguiente', async () => {
    const onVerMas = vi.fn()
    const user = userEvent.setup()
    render(<BotonVerMas {...base} cargadas={50} total={120} onVerMas={onVerMas} />)

    await user.click(screen.getByRole('button', { name: 'Ver más' }))

    expect(onVerMas).toHaveBeenCalledTimes(1)
  })

  it('mientras carga se deshabilita para no pedir dos veces', () => {
    render(<BotonVerMas {...base} cargadas={50} total={120} cargando />)

    expect(screen.getByRole('button', { name: 'Cargando…' })).toBeDisabled()
  })

  it('avisa si falló y deja el botón para reintentar', () => {
    render(<BotonVerMas {...base} cargadas={50} total={120} error />)

    expect(screen.getByRole('alert')).toHaveTextContent('No se pudieron cargar más')
    expect(screen.getByRole('button', { name: 'Ver más' })).toBeEnabled()
  })
})
