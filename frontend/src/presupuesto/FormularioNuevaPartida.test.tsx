import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { setToken } from '../api/client'
import { MOCK_TOKEN_COMPRADOR, reiniciarPresupuestoMock } from '../mocks/handlers'
import * as modoSimulado from '../mocks/modoSimulado'
import { server } from '../mocks/server'
import { FormularioNuevaPartida } from './FormularioNuevaPartida'

const OBRA_ID = 'b1b2c3d4-0000-0000-0000-0000000000a1'

async function completar(
  user: ReturnType<typeof userEvent.setup>,
  valores: { codigo?: string; descripcion?: string; etapa?: string; monto?: string },
) {
  if (valores.codigo !== undefined) {
    await user.type(screen.getByLabelText('Código de la partida'), valores.codigo)
  }
  if (valores.descripcion !== undefined) {
    await user.type(screen.getByLabelText('Descripción'), valores.descripcion)
  }
  if (valores.etapa !== undefined) {
    await user.type(screen.getByLabelText(/Etapa/), valores.etapa)
  }
  if (valores.monto !== undefined) {
    await user.type(screen.getByLabelText(/Monto asignado/), valores.monto)
  }
}

describe('FormularioNuevaPartida', () => {
  beforeEach(() => {
    localStorage.clear()
    setToken(MOCK_TOKEN_COMPRADOR)
    reiniciarPresupuestoMock()
    vi.restoreAllMocks()
    vi.spyOn(modoSimulado, 'estaEnModoSimulado').mockReturnValue(true)
  })

  it('envía la partida con el monto bien convertido y avisa a la pantalla', async () => {
    const user = userEvent.setup()
    const onCreada = vi.fn()
    let cuerpoRecibido: Record<string, unknown> | null = null
    server.use(
      http.post('*/api/presupuestos/partidas', async ({ request }) => {
        cuerpoRecibido = (await request.json()) as Record<string, unknown>
        return HttpResponse.json(
          {
            id: 'nueva-1',
            obraId: OBRA_ID,
            etapa: 'Terminaciones',
            codigoPartida: 'TER-001',
            descripcion: 'Pintura y revestimientos',
            montoAsignado: 1500000.5,
            montoComprometido: 0,
            montoEjecutado: 0,
            montoDisponible: 1500000.5,
            moneda: 'ARS',
            createdAt: '2026-11-20T12:00:00Z',
          },
          { status: 201 },
        )
      }),
    )
    render(<FormularioNuevaPartida obraId={OBRA_ID} onCreada={onCreada} />)

    await completar(user, {
      codigo: 'TER-001',
      descripcion: 'Pintura y revestimientos',
      etapa: 'Terminaciones',
      monto: '1.500.000,50',
    })
    await user.click(screen.getByRole('button', { name: 'Guardar partida' }))

    await waitFor(() => expect(onCreada).toHaveBeenCalledTimes(1))
    expect(onCreada.mock.calls[0][0].codigoPartida).toBe('TER-001')
    expect(cuerpoRecibido).toEqual({
      obraId: OBRA_ID,
      etapa: 'Terminaciones',
      codigoPartida: 'TER-001',
      descripcion: 'Pintura y revestimientos',
      montoAsignado: 1500000.5,
      moneda: 'ARS',
    })
    // el formulario queda limpio para cargar otra
    expect(screen.getByLabelText('Código de la partida')).toHaveValue('')
  })

  it('manda etapa null cuando se deja vacía (partida general de la obra)', async () => {
    const user = userEvent.setup()
    let cuerpoRecibido: Record<string, unknown> | null = null
    server.use(
      http.post('*/api/presupuestos/partidas', async ({ request }) => {
        cuerpoRecibido = (await request.json()) as Record<string, unknown>
        return HttpResponse.json({ ...cuerpoRecibido, id: 'x', montoDisponible: 0 }, { status: 201 })
      }),
    )
    render(<FormularioNuevaPartida obraId={OBRA_ID} onCreada={vi.fn()} />)

    await completar(user, { codigo: 'GEN-001', descripcion: 'General', monto: '1000' })
    await user.click(screen.getByRole('button', { name: 'Guardar partida' }))

    await waitFor(() => expect(cuerpoRecibido).not.toBeNull())
    expect(cuerpoRecibido).toMatchObject({ etapa: null, montoAsignado: 1000 })
  })

  it('no envía nada y muestra los errores si faltan datos o el monto es inválido', async () => {
    const user = userEvent.setup()
    const onCreada = vi.fn()
    let llamadas = 0
    server.use(
      http.post('*/api/presupuestos/partidas', () => {
        llamadas += 1
        return HttpResponse.json({}, { status: 201 })
      }),
    )
    render(<FormularioNuevaPartida obraId={OBRA_ID} onCreada={onCreada} />)

    await completar(user, { monto: '-5' })
    await user.click(screen.getByRole('button', { name: 'Guardar partida' }))

    expect(await screen.findByText('El código es obligatorio.')).toBeInTheDocument()
    expect(screen.getByText('La descripción es obligatoria.')).toBeInTheDocument()
    expect(screen.getByText(/Ingresá un monto válido/)).toBeInTheDocument()
    expect(llamadas).toBe(0)
    expect(onCreada).not.toHaveBeenCalled()
  })

  it('muestra el detalle del backend cuando la partida ya existe (409)', async () => {
    const user = userEvent.setup()
    const onCreada = vi.fn()
    render(<FormularioNuevaPartida obraId={OBRA_ID} onCreada={onCreada} />)

    // EST-001 ya existe en el mock de la obra Torre Alvear
    await completar(user, { codigo: 'EST-001', descripcion: 'Repetida', monto: '100' })
    await user.click(screen.getByRole('button', { name: 'Guardar partida' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Ya existe una partida con ese código o para esa etapa en la obra.',
    )
    expect(onCreada).not.toHaveBeenCalled()
  })

  it('muestra un mensaje claro si no hay conexión con el servidor', async () => {
    const user = userEvent.setup()
    server.use(http.post('*/api/presupuestos/partidas', () => HttpResponse.error()))
    render(<FormularioNuevaPartida obraId={OBRA_ID} onCreada={vi.fn()} />)

    await completar(user, { codigo: 'NET-001', descripcion: 'Sin red', monto: '100' })
    await user.click(screen.getByRole('button', { name: 'Guardar partida' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudo conectar con el servidor')
  })
})
