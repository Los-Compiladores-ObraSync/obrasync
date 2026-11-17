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

// Rechazar una requisición (#149): mismo patrón que aprobar, pero la tarjeta muestra el motivo y al confirmar
// se llama a POST /api/requisiciones/{id}/rechazar con { motivo }. El asistente nunca rechaza por su cuenta.

const RUTA_RECHAZAR = '*/api/requisiciones/:id/rechazar'
const MOTIVO = 'Faltan las cantidades de arena'

const BORRADOR_RECHAZO = {
  respuesta: 'Armé el rechazo de la REQ-2026-0042. Revisalo y tocá Rechazar.',
  borrador: null,
  aprobacion: {
    ...BORRADOR_APROBACION.aprobacion,
    accion: 'rechazarRequisicion',
    titulo: 'Rechazar requisición REQ-2026-0042',
    motivo: MOTIVO,
  },
  tokensUsados: 10,
}

async function pedirRechazo(user: ReturnType<typeof userEvent.setup>, borrador: typeof BORRADOR_RECHAZO = BORRADOR_RECHAZO) {
  server.use(http.post(RUTA_CHAT, () => HttpResponse.json(borrador)))
  await user.type(screen.getByLabelText('Escribí tu mensaje'), 'rechazá la REQ-2026-0042, faltan las cantidades')
  await user.click(screen.getByRole('button', { name: 'Enviar' }))
}

/** Cuenta los POST /rechazar y guarda el cuerpo que llegó. Responde 200 salvo que se le pida otro estado. */
function espiarRechazo(estado = 200, detalle = 'Solo se pueden rechazar requisiciones pendientes.') {
  const visto = { llamadas: 0, id: null as string | null, cuerpo: null as unknown }
  server.use(
    http.post(RUTA_RECHAZAR, async ({ params, request }) => {
      visto.llamadas += 1
      visto.id = String(params.id)
      visto.cuerpo = await request.json()
      if (estado !== 200) {
        return HttpResponse.json({ title: 'Error', status: estado, detail: detalle }, { status: estado })
      }
      return HttpResponse.json(
        { requisicionId: ID_REQUISICION, numeroRequisicion: 'REQ-2026-0042', estado: 'Rechazada', items: [] },
        { status: 200 },
      )
    }),
  )
  return visto
}

describe('Rechazar una requisición desde el chat de Compras (#149)', () => {
  it('ofrece rechazar entre lo que sabe hacer', () => {
    renderCompras()

    expect(screen.getByRole('button', { name: /Rechazar una requisición/ })).toBeEnabled()
  })

  it('arma la tarjeta con el motivo y NO rechaza hasta que se toca Rechazar', async () => {
    const user = userEvent.setup()
    const visto = espiarRechazo()
    renderCompras()

    await pedirRechazo(user)

    expect(await screen.findByText('Rechazar requisición REQ-2026-0042')).toBeInTheDocument()
    expect(screen.getByText('Requisición: REQ-2026-0042')).toBeInTheDocument()
    expect(screen.getByText(`Motivo: ${MOTIVO}`)).toBeInTheDocument()
    expect(screen.getByText(/20 bolsa · Cemento Portland 50 kg/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Rechazar' })).toBeEnabled()
    // Es una acción que no se puede deshacer: la tarjeta lo avisa.
    expect(screen.getByText(/no se puede deshacer/)).toBeInTheDocument()
    expect(visto.llamadas).toBe(0)
  })

  it('al tocar Rechazar llama a POST /rechazar una vez, con el id y el motivo del borrador', async () => {
    const user = userEvent.setup()
    const visto = espiarRechazo()
    const aprobar = espiarAprobacion()
    renderCompras()
    await pedirRechazo(user)

    await user.click(await screen.findByRole('button', { name: 'Rechazar' }))

    expect(await screen.findByText('Listo, rechacé la requisición REQ-2026-0042.')).toBeInTheDocument()
    expect(visto.llamadas).toBe(1)
    expect(visto.id).toBe(ID_REQUISICION)
    expect(visto.cuerpo).toEqual({ motivo: MOTIVO })
    // No se aprobó nada por error.
    expect(aprobar.llamadas).toBe(0)
    // La tarjeta desaparece: no se puede rechazar dos veces.
    expect(screen.queryByRole('button', { name: 'Rechazar' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Ver otras pendientes' })).toBeInTheDocument()
  })

  it('un doble toque en Rechazar manda la acción una sola vez', async () => {
    const user = userEvent.setup()
    const visto = espiarRechazo()
    renderCompras()
    await pedirRechazo(user)

    await user.dblClick(await screen.findByRole('button', { name: 'Rechazar' }))

    expect(await screen.findByText(/Listo, rechacé/)).toBeInTheDocument()
    expect(visto.llamadas).toBe(1)
  })

  it('al cancelar no se rechaza nada y la requisición sigue pendiente', async () => {
    const user = userEvent.setup()
    const visto = espiarRechazo()
    renderCompras()
    await pedirRechazo(user)

    await user.click(await screen.findByRole('button', { name: 'Cancelar' }))

    expect(await screen.findByText(/no rechacé nada/)).toBeInTheDocument()
    expect(visto.llamadas).toBe(0)
    expect(screen.queryByRole('button', { name: 'Rechazar' })).not.toBeInTheDocument()
  })

  it('si ya no está pendiente (409) lo explica, no dice que la rechazó y deja reintentar o cancelar', async () => {
    const user = userEvent.setup()
    const visto = espiarRechazo(409)
    renderCompras()
    await pedirRechazo(user)

    await user.click(await screen.findByRole('button', { name: 'Rechazar' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Solo se pueden rechazar requisiciones pendientes.')
    expect(screen.queryByText(/Listo, rechacé/)).not.toBeInTheDocument()
    expect(visto.llamadas).toBe(1)
    expect(screen.getByRole('button', { name: 'Rechazar' })).toBeEnabled()
  })

  it('si el servidor rechaza el motivo (400) muestra su explicación', async () => {
    const user = userEvent.setup()
    espiarRechazo(400, 'El motivo de rechazo es obligatorio y no puede superar los 500 caracteres.')
    renderCompras()
    await pedirRechazo(user)

    await user.click(await screen.findByRole('button', { name: 'Rechazar' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('El motivo de rechazo es obligatorio')
    expect(screen.queryByText(/Listo, rechacé/)).not.toBeInTheDocument()
  })

  it('un usuario sin permiso (403) lo ve en la tarjeta', async () => {
    const user = userEvent.setup()
    espiarRechazo(403)
    renderCompras()
    await pedirRechazo(user)

    await user.click(await screen.findByRole('button', { name: 'Rechazar' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('No tenés permiso para hacer esto.')
  })

  it('muestra el motivo como TEXTO, nunca como HTML (#174)', async () => {
    const user = userEvent.setup()
    espiarRechazo()
    const { container } = renderCompras()

    await pedirRechazo(user, {
      ...BORRADOR_RECHAZO,
      aprobacion: { ...BORRADOR_RECHAZO.aprobacion, motivo: '<img src=x onerror=alert(1)> precio alto' },
    })

    expect(await screen.findByText(/<img src=x onerror=alert\(1\)> precio alto/)).toBeInTheDocument()
    expect(container.querySelector('img')).toBeNull()
  })

  it('una aprobación sigue mostrando Aprobar y sin motivo', async () => {
    const user = userEvent.setup()
    espiarAprobacion()
    renderCompras()

    await pedirAprobacion(user)

    expect(await screen.findByRole('button', { name: 'Aprobar' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Rechazar' })).not.toBeInTheDocument()
    expect(screen.queryByText(/Motivo:/)).not.toBeInTheDocument()
  })
})
