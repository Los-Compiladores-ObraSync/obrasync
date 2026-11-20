import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { reiniciarLicitacionesMock } from '../mocks/handlers'
import { AccionesLicitacionAbierta } from './AccionesLicitacionAbierta'
import type { LicitacionResponseDto } from './licitacionesTipos'

// Es la licitación abierta del mock (REQ-2026-0011): vence el 20/11/2026.
const licitacion: LicitacionResponseDto = {
  licitacionId: 'b1b2c3d4-1111-0000-0000-000000000001',
  requisicionId: 'd1d2d3d4-0000-0000-0000-000000000011',
  numeroRequisicion: 'REQ-2026-0011',
  obraId: 'b1b2c3d4-0000-0000-0000-0000000000a1',
  estado: 'Abierta',
  fechaCreacion: '2026-10-20T10:00:00Z',
  fechaLimiteCotizacion: '2026-11-20T18:00:00Z',
  items: [],
}

describe('AccionesLicitacionAbierta', () => {
  beforeEach(() => reiniciarLicitacionesMock())

  it('extiende el plazo y avisa la nueva fecha', async () => {
    const user = userEvent.setup()
    const onActualizada = vi.fn()
    render(<AccionesLicitacionAbierta licitacion={licitacion} onActualizada={onActualizada} />)

    fireEvent.change(screen.getByLabelText('Dar más tiempo hasta'), { target: { value: '2026-12-05' } })
    await user.click(screen.getByRole('button', { name: 'Extender plazo' }))

    expect(await screen.findByRole('status')).toHaveTextContent('Listo: los proveedores pueden cotizar hasta')
    expect(onActualizada).toHaveBeenCalledTimes(1)
  })

  it('no deja extender a una fecha que no es posterior: muestra el aviso del servidor', async () => {
    const user = userEvent.setup()
    const onActualizada = vi.fn()
    render(<AccionesLicitacionAbierta licitacion={licitacion} onActualizada={onActualizada} />)

    fireEvent.change(screen.getByLabelText('Dar más tiempo hasta'), { target: { value: '2026-11-10' } })
    await user.click(screen.getByRole('button', { name: 'Extender plazo' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('posterior a la actual')
    expect(onActualizada).not.toHaveBeenCalled()
  })

  it('cerrar antes de tiempo exige el motivo y avisa cuando sale bien', async () => {
    const user = userEvent.setup()
    const onActualizada = vi.fn()
    render(<AccionesLicitacionAbierta licitacion={licitacion} onActualizada={onActualizada} />)

    await user.click(screen.getByRole('button', { name: 'Cerrar antes de tiempo' }))
    await user.click(screen.getByRole('button', { name: 'Sí, cerrar la licitación' }))
    expect(screen.getByRole('alert')).toHaveTextContent('Contá por qué cerrás')
    expect(onActualizada).not.toHaveBeenCalled()

    await user.type(screen.getByLabelText('Motivo del cierre'), 'Cambió el diseño de la obra')
    await user.click(screen.getByRole('button', { name: 'Sí, cerrar la licitación' }))

    await vi.waitFor(() => expect(onActualizada).toHaveBeenCalledTimes(1))
    expect(onActualizada.mock.calls[0][0]).toMatchObject({ estado: 'Cerrada' })
  })

  it('"No, dejarla abierta" vuelve atrás sin llamar al servidor', async () => {
    const user = userEvent.setup()
    const onActualizada = vi.fn()
    render(<AccionesLicitacionAbierta licitacion={licitacion} onActualizada={onActualizada} />)

    await user.click(screen.getByRole('button', { name: 'Cerrar antes de tiempo' }))
    await user.click(screen.getByRole('button', { name: 'No, dejarla abierta' }))

    expect(screen.getByRole('button', { name: 'Cerrar antes de tiempo' })).toBeInTheDocument()
    expect(onActualizada).not.toHaveBeenCalled()
  })
})
