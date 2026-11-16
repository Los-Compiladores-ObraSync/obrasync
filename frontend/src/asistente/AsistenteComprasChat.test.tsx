import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import { server } from '../mocks/server'
import { AsistenteChatPage } from './AsistenteChatPage'

// Asistente en el portal de Compras (#141): consulta requisiciones y arma la APROBACIÓN para confirmar.
// Regla de oro: el asistente nunca aprueba; solo el botón Aprobar llama a POST /api/requisiciones/{id}/aprobar.

const RUTA_CHAT = '*/api/asistente/chat'
const RUTA_APROBAR = '*/api/requisiciones/:id/aprobar'
const ID_REQUISICION = 'd1d2d3d4-0000-0000-0000-000000000042'

const BORRADOR_APROBACION = {
  respuesta: 'Armé la aprobación de la REQ-2026-0042. Revisala y tocá Aprobar.',
  borrador: null,
  aprobacion: {
    accion: 'aprobarRequisicion',
    titulo: 'Aprobar requisición REQ-2026-0042',
    requisicionId: ID_REQUISICION,
    resumen: {
      numero: 'REQ-2026-0042',
      obra: 'Torre Central',
      etapa: 'Estructura',
      items: [{ descripcion: 'Cemento Portland 50 kg', cantidad: 20, unidadMedida: 'BOLSA' }],
    },
  },
  tokensUsados: 10,
}

function renderCompras() {
  return render(
    <MemoryRouter>
      <AsistenteChatPage portal="compras" />
    </MemoryRouter>,
  )
}

async function pedirAprobacion(user: ReturnType<typeof userEvent.setup>) {
  server.use(http.post(RUTA_CHAT, () => HttpResponse.json(BORRADOR_APROBACION)))
  await user.type(screen.getByLabelText('Escribí tu mensaje'), 'aprobá la REQ-2026-0042')
  await user.click(screen.getByRole('button', { name: 'Enviar' }))
}

/** Cuenta los POST /aprobar. Responde 200 salvo que se le pida otro estado. */
function espiarAprobacion(estado = 200) {
  const visto = { llamadas: 0, id: null as string | null }
  server.use(
    http.post(RUTA_APROBAR, ({ params }) => {
      visto.llamadas += 1
      visto.id = String(params.id)
      if (estado !== 200) {
        return HttpResponse.json(
          { title: 'Conflicto', status: estado, detail: 'Solo se pueden aprobar requisiciones pendientes.' },
          { status: estado },
        )
      }
      return HttpResponse.json(
        {
          requisicionId: ID_REQUISICION,
          numeroRequisicion: 'REQ-2026-0042',
          estado: 'Aprobada',
          items: [],
        },
        { status: 200 },
      )
    }),
  )
  return visto
}

describe('AsistenteChatPage en el portal de Compras', () => {
  it('ofrece lo de Compras (no lo del Capataz) y enlaza a la pantalla de aprobar', () => {
    renderCompras()

    expect(screen.getByRole('button', { name: /Ver requisiciones pendientes/ })).toBeEnabled()
    expect(screen.getByRole('button', { name: /Aprobar una requisición/ })).toBeEnabled()
    expect(screen.queryByText('Pedir materiales')).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Ir a aprobar requisiciones' })).toHaveAttribute(
      'href',
      '/compras/requisiciones',
    )
    expect(screen.queryByRole('link', { name: 'Cargar una requisición a mano' })).not.toBeInTheDocument()
  })

  it('arma la tarjeta de aprobación con el resumen y NO aprueba hasta que se toca Aprobar', async () => {
    const user = userEvent.setup()
    const visto = espiarAprobacion()
    renderCompras()

    await pedirAprobacion(user)

    expect(await screen.findByText('Aprobar requisición REQ-2026-0042')).toBeInTheDocument()
    expect(screen.getByText('Requisición: REQ-2026-0042')).toBeInTheDocument()
    expect(screen.getByText('Obra: Torre Central')).toBeInTheDocument()
    expect(screen.getByText('Etapa: Estructura')).toBeInTheDocument()
    expect(screen.getByText(/20 bolsa · Cemento Portland 50 kg/)).toBeInTheDocument()
    expect(visto.llamadas).toBe(0)
  })

  it('al tocar Aprobar llama a POST /aprobar una sola vez con el id del borrador y lo confirma', async () => {
    const user = userEvent.setup()
    const visto = espiarAprobacion()
    renderCompras()
    await pedirAprobacion(user)

    await user.click(await screen.findByRole('button', { name: 'Aprobar' }))

    expect(await screen.findByText('Listo, aprobé la requisición REQ-2026-0042.')).toBeInTheDocument()
    expect(visto.llamadas).toBe(1)
    expect(visto.id).toBe(ID_REQUISICION)
    // La tarjeta desaparece: no se puede aprobar dos veces.
    expect(screen.queryByRole('button', { name: 'Aprobar' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Ver otras pendientes' })).toBeInTheDocument()
  })

  it('al cancelar no se aprueba nada y la requisición sigue pendiente', async () => {
    const user = userEvent.setup()
    const visto = espiarAprobacion()
    renderCompras()
    await pedirAprobacion(user)

    await user.click(await screen.findByRole('button', { name: 'Cancelar' }))

    expect(await screen.findByText(/no aprobé nada/)).toBeInTheDocument()
    expect(visto.llamadas).toBe(0)
    expect(screen.queryByRole('button', { name: 'Aprobar' })).not.toBeInTheDocument()
  })

  it('si otra persona ya la resolvió (409) lo avisa y no dice que la aprobó', async () => {
    const user = userEvent.setup()
    const visto = espiarAprobacion(409)
    renderCompras()
    await pedirAprobacion(user)

    await user.click(await screen.findByRole('button', { name: 'Aprobar' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Solo se pueden aprobar requisiciones pendientes.')
    expect(screen.queryByText(/Listo, aprobé/)).not.toBeInTheDocument()
    expect(visto.llamadas).toBe(1)
    // Sigue la tarjeta para cancelar o reintentar.
    expect(screen.getByRole('button', { name: 'Aprobar' })).toBeEnabled()
  })

  it('una consulta sin borrador solo muestra texto, sin tarjeta de confirmación', async () => {
    const user = userEvent.setup()
    const visto = espiarAprobacion()
    server.use(
      http.post(RUTA_CHAT, () =>
        HttpResponse.json({ respuesta: 'Hay 2 requisiciones pendientes.', borrador: null, aprobacion: null }),
      ),
    )
    renderCompras()

    await user.click(screen.getByRole('button', { name: /Ver requisiciones pendientes/ }))

    expect(await screen.findByText('Hay 2 requisiciones pendientes.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Aprobar' })).not.toBeInTheDocument()
    expect(visto.llamadas).toBe(0)
  })

  it('el texto del asistente se muestra siempre como texto, nunca como HTML (#174)', async () => {
    const user = userEvent.setup()
    server.use(
      http.post(RUTA_CHAT, () =>
        HttpResponse.json({
          respuesta: '<img src=x onerror=alert(1)> Observación de la requisición',
          borrador: null,
        }),
      ),
    )
    const { container } = renderCompras()

    await user.click(screen.getByRole('button', { name: /Ver requisiciones pendientes/ }))

    expect(await screen.findByText(/<img src=x onerror=alert\(1\)>/)).toBeInTheDocument()
    expect(container.querySelector('img')).toBeNull()
  })

  it('muestra como TEXTO la obra y los ítems de la tarjeta de aprobación (datos del servidor) (#174)', async () => {
    const user = userEvent.setup()
    server.use(
      http.post(RUTA_CHAT, () =>
        HttpResponse.json({
          ...BORRADOR_APROBACION,
          aprobacion: {
            ...BORRADOR_APROBACION.aprobacion,
            resumen: {
              ...BORRADOR_APROBACION.aprobacion.resumen,
              obra: '<script>alert(1)</script> Torre',
              items: [{ descripcion: '<img src=x onerror=alert(2)> Cemento', cantidad: 1, unidadMedida: 'BOLSA' }],
            },
          },
        }),
      ),
    )
    const { container } = renderCompras()

    await user.type(screen.getByLabelText('Escribí tu mensaje'), 'aprobá la REQ-2026-0042')
    await user.click(screen.getByRole('button', { name: 'Enviar' }))

    expect(await screen.findByText(/<script>alert\(1\)<\/script> Torre/)).toBeInTheDocument()
    expect(screen.getByText(/<img src=x onerror=alert\(2\)> Cemento/)).toBeInTheDocument()
    expect(container.querySelector('img')).toBeNull()
    expect(container.querySelector('script')).toBeNull()
  })

  it('un 503 sugiere la pantalla de aprobar, no el formulario de obra', async () => {
    const user = userEvent.setup()
    server.use(http.post(RUTA_CHAT, () => HttpResponse.json({ title: 'Error', status: 503 }, { status: 503 })))
    renderCompras()

    await user.click(screen.getByRole('button', { name: /Ver requisiciones pendientes/ }))

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('pantalla de requisiciones'))
  })
})
