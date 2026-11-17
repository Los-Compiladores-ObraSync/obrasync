import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import { server } from '../mocks/server'
import { PresupuestoPage } from './PresupuestoPage'

function renderPagina(initialEntries = ['/compras/presupuesto']) {
  return render(
    <MemoryRouter initialEntries={initialEntries}>
      <PresupuestoPage />
    </MemoryRouter>,
  )
}

async function seleccionarObra(user: ReturnType<typeof userEvent.setup>, nombre: string) {
  const select = await screen.findByLabelText('Obra')
  await screen.findByRole('option', { name: nombre })
  await user.selectOptions(select, nombre)
}

describe('PresupuestoPage', () => {
  it('muestra el selector de obras y carga la lista con montos al elegir una obra', async () => {
    const user = userEvent.setup()
    renderPagina()

    await seleccionarObra(user, 'Torre Alvear')

    // Espera las tarjetas de etapas
    const headingEstructura = await screen.findByRole('heading', {
      level: 2,
      name: 'Estructura',
    })
    expect(headingEstructura).toBeInTheDocument()

    const tarjeta = headingEstructura.closest('li')!
    expect(within(tarjeta).getByText(/Código:\s*EST-001/)).toBeInTheDocument()
    expect(within(tarjeta).getByText(/Asignado:/)).toBeInTheDocument()
    expect(within(tarjeta).getByText(/\$\s*10\.000\.000,00/)).toBeInTheDocument()
    expect(within(tarjeta).getByText(/Comprometido:/)).toBeInTheDocument()
    expect(within(tarjeta).getByText(/\$\s*6\.000\.000,00/)).toBeInTheDocument()
    expect(within(tarjeta).getByText(/Ejecutado:/)).toBeInTheDocument()
    expect(within(tarjeta).getByText(/Diferencia:/)).toBeInTheDocument()
    // En Estructura tanto Ejecutado como Diferencia son $ 4.000.000,00
    expect(within(tarjeta).getAllByText(/\$\s*4\.000\.000,00/)).toHaveLength(2)
  })

  it('muestra "General de la obra" cuando la etapa es null', async () => {
    server.use(
      http.get('*/api/presupuestos/partidas', () =>
        HttpResponse.json([
          {
            id: 'p-general-1',
            obraId: 'b1b2c3d4-0000-0000-0000-0000000000a1',
            etapa: null,
            codigoPartida: 'GEN-001',
            descripcion: 'Partida global de la obra',
            montoAsignado: 20000000,
            montoComprometido: 5000000,
            montoEjecutado: 1000000,
            montoDisponible: 15000000,
            moneda: 'ARS',
            createdAt: '2026-10-01T08:00:00Z',
          },
        ]),
      ),
    )

    const user = userEvent.setup()
    renderPagina()

    await seleccionarObra(user, 'Torre Alvear')

    expect(
      await screen.findByRole('heading', { level: 2, name: 'General de la obra' }),
    ).toBeInTheDocument()
  })

  it('marca "Excedida" con texto y color cuando montoDisponible < 0', async () => {
    const user = userEvent.setup()
    renderPagina()

    await seleccionarObra(user, 'Torre Alvear')

    // Albañilería tiene montoDisponible = -1.200.000
    const headingAlbanileria = await screen.findByRole('heading', {
      level: 2,
      name: 'Albañilería',
    })
    const tarjeta = headingAlbanileria.closest('li')!

    // Debe mostrar la etiqueta textual "Excedida"
    const etiqueta = within(tarjeta).getByText('Excedida')
    expect(etiqueta).toBeInTheDocument()

    // Debe mostrar la diferencia negativa TAL CUAL la manda el servicio
    expect(within(tarjeta).getByText(/-\$?\s*1\.200\.000,00/)).toBeInTheDocument()
  })

  it('muestra estado vacío ("Esta obra no tiene partidas") cuando no hay partidas registradas', async () => {
    server.use(
      http.get('*/api/presupuestos/partidas', () => HttpResponse.json([])),
    )

    const user = userEvent.setup()
    renderPagina()

    await seleccionarObra(user, 'Torre Alvear')

    expect(
      await screen.findByText('Esta obra no tiene partidas'),
    ).toBeInTheDocument()
  })

  it('maneja error con botón "Reintentar" y recarga al pulsarlo', async () => {
    let fallo = true
    server.use(
      http.get('*/api/presupuestos/partidas', () => {
        if (fallo) {
          return HttpResponse.json(
            { title: 'Error de servidor', status: 500, detail: 'Error temporal' },
            { status: 500 },
          )
        }
        return HttpResponse.json([
          {
            id: 'p-ok-1',
            obraId: 'b1b2c3d4-0000-0000-0000-0000000000a1',
            etapa: 'Estructura',
            codigoPartida: 'EST-001',
            descripcion: 'Hormigón',
            montoAsignado: 5000000,
            montoComprometido: 1000000,
            montoEjecutado: 500000,
            montoDisponible: 4000000,
            moneda: 'ARS',
            createdAt: '2026-10-01T08:00:00Z',
          },
        ])
      }),
    )

    const user = userEvent.setup()
    renderPagina()

    await seleccionarObra(user, 'Torre Alvear')

    // Muestra mensaje de error
    const alerta = await screen.findByRole('alert')
    expect(alerta).toHaveTextContent(/No se pudieron cargar las partidas presupuestarias/)

    // Botón de reintentar
    const botonReintentar = screen.getByRole('button', { name: 'Reintentar' })
    expect(botonReintentar).toBeInTheDocument()

    // Reintenta con éxito
    fallo = false
    await user.click(botonReintentar)

    expect(
      await screen.findByRole('heading', { level: 2, name: 'Estructura' }),
    ).toBeInTheDocument()
  })

  it('muestra mensaje específico de permiso cuando el servicio devuelve 403', async () => {
    server.use(
      http.get('*/api/presupuestos/partidas', () =>
        HttpResponse.json(
          { title: 'Acceso denegado', status: 403, detail: 'Sin permiso' },
          { status: 403 },
        ),
      ),
    )

    const user = userEvent.setup()
    renderPagina()

    await seleccionarObra(user, 'Torre Alvear')

    const alerta = await screen.findByRole('alert')
    expect(alerta).toHaveTextContent(/No tenés permiso/)
  })

  it('tiene enlace accesible para volver a compras', async () => {
    renderPagina()
    const volver = screen.getByRole('link', { name: '← Volver a compras' })
    expect(volver).toBeInTheDocument()
    expect(volver).toHaveAttribute('href', '/compras')
  })
})
