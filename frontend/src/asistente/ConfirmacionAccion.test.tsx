import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { ApiError } from '../api/ApiError'
import { ConfirmacionAccion } from './ConfirmacionAccion'

function renderTarjeta(
  props: Partial<React.ComponentProps<typeof ConfirmacionAccion>> = {},
) {
  const onConfirmar = props.onConfirmar ?? vi.fn().mockResolvedValue(undefined)
  const onCancelar = props.onCancelar ?? vi.fn()
  render(
    <ConfirmacionAccion
      titulo="Nueva requisición"
      onConfirmar={onConfirmar}
      onCancelar={onCancelar}
      {...props}
    >
      <p>20 bolsas · Cemento</p>
    </ConfirmacionAccion>,
  )
  return { onConfirmar, onCancelar }
}

describe('ConfirmacionAccion', () => {
  it('muestra el título y el resumen, y no ejecuta nada hasta que se confirma', () => {
    const { onConfirmar } = renderTarjeta()

    expect(screen.getByRole('heading', { name: 'Nueva requisición' })).toBeInTheDocument()
    expect(screen.getByText('20 bolsas · Cemento')).toBeInTheDocument()
    expect(onConfirmar).not.toHaveBeenCalled()
  })

  it('al confirmar ejecuta la acción una vez', async () => {
    const user = userEvent.setup()
    const { onConfirmar } = renderTarjeta()

    await user.click(screen.getByRole('button', { name: 'Confirmar' }))

    await waitFor(() => expect(onConfirmar).toHaveBeenCalledTimes(1))
  })

  it('cancelar no ejecuta la acción', async () => {
    const user = userEvent.setup()
    const { onConfirmar, onCancelar } = renderTarjeta()

    await user.click(screen.getByRole('button', { name: 'Cancelar' }))

    expect(onCancelar).toHaveBeenCalledTimes(1)
    expect(onConfirmar).not.toHaveBeenCalled()
  })

  it('un doble toque ejecuta la acción una sola vez y bloquea los botones mientras se envía', async () => {
    const user = userEvent.setup()
    let terminar: () => void = () => {}
    const onConfirmar = vi.fn(
      () =>
        new Promise<void>((resolver) => {
          terminar = resolver
        }),
    )
    renderTarjeta({ onConfirmar })

    const boton = screen.getByRole('button', { name: 'Confirmar' })
    await user.dblClick(boton)

    expect(onConfirmar).toHaveBeenCalledTimes(1)
    expect(screen.getByRole('button', { name: 'Enviando…' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Cancelar' })).toBeDisabled()

    terminar()
    await waitFor(() => expect(screen.getByRole('button', { name: 'Confirmar' })).toBeEnabled())
  })

  it('un 409 muestra el motivo del servidor y deja volver a intentar', async () => {
    const user = userEvent.setup()
    const onConfirmar = vi
      .fn()
      .mockRejectedValue(new ApiError(409, { detail: 'Ya existe una requisición idéntica reciente.' }))
    renderTarjeta({ onConfirmar })

    await user.click(screen.getByRole('button', { name: 'Confirmar' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Ya existe una requisición idéntica reciente.',
    )
    expect(screen.getByRole('button', { name: 'Confirmar' })).toBeEnabled()
  })

  it('un error del servidor o de red se explica sin mostrar datos técnicos', async () => {
    const user = userEvent.setup()
    const onConfirmar = vi.fn().mockRejectedValue(new TypeError('Failed to fetch'))
    renderTarjeta({ onConfirmar })

    await user.click(screen.getByRole('button', { name: 'Confirmar' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudo conectar con el servidor')
  })

  it('un 403 dice que no tiene permiso', async () => {
    const user = userEvent.setup()
    const onConfirmar = vi.fn().mockRejectedValue(new ApiError(403, {}))
    renderTarjeta({ onConfirmar })

    await user.click(screen.getByRole('button', { name: 'Confirmar' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('No tenés permiso')
  })

  it('la confirmación reforzada avisa que no se puede deshacer', () => {
    renderTarjeta({ reforzada: true })

    expect(screen.getByText(/no se puede deshacer/i)).toBeInTheDocument()
  })
})
