import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import { server } from '../mocks/server'
import { AsistenteChatPage } from './AsistenteChatPage'
import { fechaLocalEnDias, finDelDiaEnUtc } from '../licitaciones/plazoCotizacion'
import { fechaLarga } from '../requisiciones/formatoFecha'

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

// Publicar la licitación de una requisición Aprobada (#149): el asistente arma el borrador con los días de plazo; la fecha
// límite la calcula el frontend en hora local (vence a las 23:59, igual que la pantalla de la requisición) y recién al tocar
// Publicar se llama a POST /api/licitaciones.

const RUTA_PUBLICAR = '*/api/licitaciones'
const DIAS_PLAZO = 5

const BORRADOR_PUBLICACION = {
  respuesta: 'Armé la publicación de la REQ-2026-0042. Revisala y tocá Publicar.',
  borrador: null,
  aprobacion: {
    ...BORRADOR_APROBACION.aprobacion,
    accion: 'publicarLicitacion',
    titulo: 'Publicar licitación de la requisición REQ-2026-0042',
    diasParaCotizar: DIAS_PLAZO,
  },
  tokensUsados: 10,
}

async function pedirPublicacion(user: ReturnType<typeof userEvent.setup>, borrador: unknown = BORRADOR_PUBLICACION) {
  server.use(http.post(RUTA_CHAT, () => HttpResponse.json(borrador as Record<string, unknown>)))
  await user.type(screen.getByLabelText('Escribí tu mensaje'), 'publicá la licitación de la REQ-2026-0042, 5 días')
  await user.click(screen.getByRole('button', { name: 'Enviar' }))
}

/** Cuenta los POST /licitaciones y guarda el cuerpo. Responde 201 salvo que se le pida otro estado. */
function espiarPublicacion(estado = 201, detalle = 'Ya existe una licitación abierta para esa requisición.') {
  const visto = { llamadas: 0, cuerpo: null as null | { requisicionId: string; fechaLimiteCotizacion: string } }
  server.use(
    http.post(RUTA_PUBLICAR, async ({ request }) => {
      visto.llamadas += 1
      visto.cuerpo = (await request.json()) as { requisicionId: string; fechaLimiteCotizacion: string }
      if (estado !== 201) {
        return HttpResponse.json({ title: 'Error', status: estado, detail: detalle }, { status: estado })
      }
      return HttpResponse.json(
        {
          licitacionId: 'e1e2e3e4-0000-0000-0000-000000000001',
          requisicionId: ID_REQUISICION,
          numeroRequisicion: 'REQ-2026-0042',
          obraId: 'b1b2b3b4-0000-0000-0000-000000000001',
          estado: 'Abierta',
          fechaCreacion: new Date().toISOString(),
          fechaLimiteCotizacion: visto.cuerpo.fechaLimiteCotizacion,
          items: [],
        },
        { status: 201 },
      )
    }),
  )
  return visto
}

describe('Publicar una licitación desde el chat de Compras (#149)', () => {
  it('ofrece publicar entre lo que sabe hacer', () => {
    renderCompras()

    expect(screen.getByRole('button', { name: /Publicar una licitación/ })).toBeEnabled()
  })

  it('arma la tarjeta con la fecha límite y NO publica hasta que se toca Publicar', async () => {
    const user = userEvent.setup()
    const visto = espiarPublicacion()
    renderCompras()

    await pedirPublicacion(user)

    expect(await screen.findByText('Publicar licitación de la requisición REQ-2026-0042')).toBeInTheDocument()
    expect(screen.getByText('Requisición: REQ-2026-0042')).toBeInTheDocument()
    const hasta = fechaLarga(finDelDiaEnUtc(fechaLocalEnDias(DIAS_PLAZO)))
    expect(screen.getByText(`Los proveedores cotizan hasta el ${hasta}`)).toBeInTheDocument()
    expect(screen.getByText(/20 bolsa · Cemento Portland 50 kg/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Publicar' })).toBeEnabled()
    // No se afirma que sea irreversible: no sabemos si la licitación se puede cancelar.
    expect(screen.queryByText(/no se puede deshacer/)).not.toBeInTheDocument()
    expect(visto.llamadas).toBe(0)
  })

  it('al tocar Publicar llama a POST /licitaciones una vez, con el id y la fecha límite a las 23:59 hora local', async () => {
    const user = userEvent.setup()
    const visto = espiarPublicacion()
    const aprobar = espiarAprobacion()
    const rechazar = espiarRechazo()
    renderCompras()
    await pedirPublicacion(user)

    await user.click(await screen.findByRole('button', { name: 'Publicar' }))

    const hasta = fechaLarga(finDelDiaEnUtc(fechaLocalEnDias(DIAS_PLAZO)))
    expect(
      await screen.findByText(
        `Listo, publiqué la licitación de la requisición REQ-2026-0042. Los proveedores pueden cotizar hasta el ${hasta}.`,
      ),
    ).toBeInTheDocument()
    expect(visto.llamadas).toBe(1)
    expect(visto.cuerpo?.requisicionId).toBe(ID_REQUISICION)
    expect(visto.cuerpo?.fechaLimiteCotizacion).toBe(finDelDiaEnUtc(fechaLocalEnDias(DIAS_PLAZO)))
    // Es futura y vence a las 23:59 de la hora local (la misma regla que la pantalla de la requisición).
    const limite = new Date(visto.cuerpo!.fechaLimiteCotizacion)
    expect(limite.getTime()).toBeGreaterThan(Date.now())
    expect(limite.getHours()).toBe(23)
    expect(limite.getMinutes()).toBe(59)
    // No se aprobó ni se rechazó nada por error.
    expect(aprobar.llamadas).toBe(0)
    expect(rechazar.llamadas).toBe(0)
    // La tarjeta desaparece: no se puede publicar dos veces.
    expect(screen.queryByRole('button', { name: 'Publicar' })).not.toBeInTheDocument()
  })

  it('un doble toque en Publicar manda la acción una sola vez', async () => {
    const user = userEvent.setup()
    const visto = espiarPublicacion()
    renderCompras()
    await pedirPublicacion(user)

    await user.dblClick(await screen.findByRole('button', { name: 'Publicar' }))

    expect(await screen.findByText(/Listo, publiqué/)).toBeInTheDocument()
    expect(visto.llamadas).toBe(1)
  })

  it('al cancelar no se publica nada', async () => {
    const user = userEvent.setup()
    const visto = espiarPublicacion()
    renderCompras()
    await pedirPublicacion(user)

    await user.click(await screen.findByRole('button', { name: 'Cancelar' }))

    expect(await screen.findByText(/no publiqué nada/)).toBeInTheDocument()
    expect(visto.llamadas).toBe(0)
    expect(screen.queryByRole('button', { name: 'Publicar' })).not.toBeInTheDocument()
  })

  it('si ya hay una licitación abierta (409) lo explica y no dice que la publicó', async () => {
    const user = userEvent.setup()
    const visto = espiarPublicacion(409)
    renderCompras()
    await pedirPublicacion(user)

    await user.click(await screen.findByRole('button', { name: 'Publicar' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Ya existe una licitación abierta para esa requisición.')
    expect(screen.queryByText(/Listo, publiqué/)).not.toBeInTheDocument()
    expect(visto.llamadas).toBe(1)
    expect(screen.getByRole('button', { name: 'Publicar' })).toBeEnabled()
  })

  it('si el servidor dice que la requisición no está aprobada (404) muestra su explicación', async () => {
    const user = userEvent.setup()
    espiarPublicacion(404, 'La requisición no existe o todavía no está aprobada.')
    renderCompras()
    await pedirPublicacion(user)

    await user.click(await screen.findByRole('button', { name: 'Publicar' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('todavía no está aprobada')
    expect(screen.queryByText(/Listo, publiqué/)).not.toBeInTheDocument()
  })

  it('un usuario sin permiso (403) lo ve en la tarjeta', async () => {
    const user = userEvent.setup()
    espiarPublicacion(403)
    renderCompras()
    await pedirPublicacion(user)

    await user.click(await screen.findByRole('button', { name: 'Publicar' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('No tenés permiso para hacer esto.')
  })

  it('si el borrador no trae los días de plazo no ofrece la tarjeta y no publica nada', async () => {
    const user = userEvent.setup()
    const visto = espiarPublicacion()
    renderCompras()

    await pedirPublicacion(user, {
      ...BORRADOR_PUBLICACION,
      aprobacion: { ...BORRADOR_PUBLICACION.aprobacion, diasParaCotizar: null },
    })

    expect(await screen.findByText(/Armé la publicación/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Publicar' })).not.toBeInTheDocument()
    expect(visto.llamadas).toBe(0)
  })
})
