import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import type { Aviso } from './avisosFlujo'
import type { Novedad } from './novedades'
import { PanelNotificaciones } from './PanelNotificaciones'

function aviso(cambios: Partial<Aviso> & Pick<Aviso, 'id' | 'tipo' | 'seccion' | 'texto'>): Aviso {
  return { marca: '', textoCorto: cambios.texto, ruta: '/compras/ordenes', ...cambios }
}

const grupo = { id: 'o-1', titulo: 'Orden 1A2B3C4D' }

const problema = aviso({
  id: 'orden-problema:o-1',
  tipo: 'orden-problema',
  seccion: 'para-hacer',
  texto: 'Entrega con problema en la orden 1A2B3C4D',
  textoCorto: 'Entrega con problema',
  detalle: 'Llegó dañada',
  grupo,
})
const chat = aviso({
  id: 'orden-chat:o-1',
  tipo: 'orden-chat',
  seccion: 'para-hacer',
  texto: '2 mensajes sin leer en el chat de la orden 1A2B3C4D',
  textoCorto: '2 mensajes sin leer en el chat',
  ruta: '/compras/ordenes',
  grupo,
})
const despacho = aviso({
  id: 'orden-despachada:o-2',
  marca: '2026-11-20T10:00:00Z',
  tipo: 'orden-despachada',
  seccion: 'novedades',
  texto: 'El proveedor despachó la orden 9Z9Z9Z9Z',
  ruta: '/compras/ordenes',
})

function renderizar(props: Partial<Parameters<typeof PanelNotificaciones>[0]> = {}) {
  const onNavegar = vi.fn()
  const onMarcarAvisos = vi.fn()
  const onMarcarNovedades = vi.fn()
  render(
    <MemoryRouter>
      <PanelNotificaciones
        id="panel"
        onNavegar={onNavegar}
        onMarcarAvisos={onMarcarAvisos}
        onMarcarNovedades={onMarcarNovedades}
        {...props}
      />
    </MemoryRouter>,
  )
  return { onNavegar, onMarcarAvisos, onMarcarNovedades }
}

describe('PanelNotificaciones: secciones "Para hacer" y "Novedades" (#219)', () => {
  it('separa lo que hay para hacer de las novedades, cada una con su título', () => {
    renderizar({ avisosParaHacer: [problema], avisosNovedades: [despacho] })

    expect(screen.getByRole('heading', { name: 'Para hacer' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Novedades' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Entrega con problema en la orden 1A2B3C4D/ })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'El proveedor despachó la orden 9Z9Z9Z9Z' })).toBeInTheDocument()
  })

  it('sin avisos de una sección, no muestra su título', () => {
    renderizar({ avisosParaHacer: [problema] })

    expect(screen.getByRole('heading', { name: 'Para hacer' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Novedades' })).not.toBeInTheDocument()
  })

  it('junta bajo el nombre de la orden los avisos de la misma orden', () => {
    renderizar({ avisosParaHacer: [problema, chat] })

    expect(screen.getByText('Orden 1A2B3C4D')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Entrega con problema/ })).toHaveAttribute('href', '/compras/ordenes')
    expect(screen.getByRole('link', { name: '2 mensajes sin leer en el chat' })).toBeInTheDocument()
  })

  it('muestra el dato de apoyo (el motivo del problema)', () => {
    renderizar({ avisosParaHacer: [problema] })

    expect(screen.getByText('Llegó dañada')).toBeInTheDocument()
  })

  it('al abrir una novedad la marca como vista y cierra el panel; lo "para hacer" no se marca', async () => {
    const user = userEvent.setup()
    const { onMarcarAvisos, onNavegar } = renderizar({ avisosParaHacer: [problema], avisosNovedades: [despacho] })

    await user.click(screen.getByRole('link', { name: /Entrega con problema/ }))
    expect(onMarcarAvisos).not.toHaveBeenCalled()
    expect(onNavegar).toHaveBeenCalledTimes(1)

    await user.click(screen.getByRole('link', { name: 'El proveedor despachó la orden 9Z9Z9Z9Z' }))
    expect(onMarcarAvisos).toHaveBeenCalledWith([despacho])
  })

  it('"Marcar como vistas" marca las novedades de las dos listas', async () => {
    const user = userEvent.setup()
    const antiguaNovedad: Novedad = {
      id: 'a',
      marca: 'Aprobada',
      tipo: 'requisicion-aprobada',
      numero: 'REQ-1',
      ruta: '/obra/requisiciones/a',
    }
    const { onMarcarAvisos, onMarcarNovedades } = renderizar({ novedades: [antiguaNovedad], avisosNovedades: [despacho] })

    await user.click(screen.getByRole('button', { name: 'Marcar como vistas' }))

    expect(onMarcarNovedades).toHaveBeenCalledWith()
    expect(onMarcarAvisos).toHaveBeenCalledWith()
  })

  it('limita los renglones para que entre en el celular y avisa cuántos quedan', () => {
    const muchos = Array.from({ length: 6 }, (_, i) =>
      aviso({ id: `orden-chat:o-${i}`, tipo: 'orden-chat', seccion: 'para-hacer', texto: `Aviso número ${i}`, grupo: { id: `o-${i}`, titulo: `Orden ${i}` } }),
    )
    renderizar({ avisosParaHacer: muchos })

    const panel = screen.getByRole('region', { name: 'Avisos' })
    expect(within(panel).getAllByRole('link')).toHaveLength(4)
    expect(screen.getByText('y 2 más')).toBeInTheDocument()
  })

  it('el motivo del rechazo se ve en la novedad del Capataz', () => {
    const rechazada: Novedad = {
      id: 'b',
      marca: 'Rechazada',
      tipo: 'requisicion-rechazada',
      numero: 'REQ-2',
      ruta: '/obra/requisiciones/b',
      detalle: 'Falta el plano',
    }
    renderizar({ novedades: [rechazada] })

    expect(screen.getByText('Motivo: Falta el plano')).toBeInTheDocument()
  })

  it('sin ningún aviso dice que no hay', () => {
    renderizar()

    expect(screen.getByText('No tenés avisos')).toBeInTheDocument()
  })
})
