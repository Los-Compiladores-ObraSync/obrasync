import { render, screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { server } from '../mocks/server'
import { PresupuestoDeEtapa } from './PresupuestoDeEtapa'

const partida = {
  id: 'p1', obraId: 'o1', etapa: 'Estructura', codigoPartida: 'SEMILLA-1', descripcion: 'x',
  montoAsignado: 5_000_000, montoComprometido: 1_000_000, montoEjecutado: 400_000, montoDisponible: 4_000_000,
  moneda: 'ARS', createdAt: '2026-11-01T00:00:00Z',
}

describe('PresupuestoDeEtapa', () => {
  it('muestra lo disponible, lo comprometido y avisa si la requisición entra', async () => {
    server.use(http.get('*/api/presupuestos/partidas', () => HttpResponse.json([partida])))
    render(<PresupuestoDeEtapa obraId="o1" etapa="Estructura" estimado={1_500_000} />)

    expect(await screen.findByText(/Disponible:/)).toHaveTextContent('$ 4.000.000')
    expect(screen.getByText(/Comprometido \$ 1\.000\.000 · Ejecutado \$ 400\.000/)).toBeInTheDocument()
    expect(screen.getByText(/entra en el presupuesto/)).toBeInTheDocument()
    expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '20')
  })

  it('alerta si lo estimado supera lo disponible', async () => {
    server.use(http.get('*/api/presupuestos/partidas', () => HttpResponse.json([partida])))
    render(<PresupuestoDeEtapa obraId="o1" etapa="Estructura" estimado={4_500_000} />)

    expect(await screen.findByRole('alert')).toHaveTextContent('quedaría bloqueada')
  })

  it('avisa si la etapa no tiene presupuesto', async () => {
    server.use(http.get('*/api/presupuestos/partidas', () => HttpResponse.json([])))
    render(<PresupuestoDeEtapa obraId="o1" etapa="Techos" />)

    expect(await screen.findByText('Esta etapa todavía no tiene presupuesto asignado.')).toBeInTheDocument()
  })
})
