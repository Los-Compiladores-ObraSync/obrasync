import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import { server } from '../mocks/server'
import { AsistenteChatPage } from './AsistenteChatPage'

const RUTA_CHAT = '*/api/asistente/chat'
const RUTA_REQUISICIONES = '*/api/requisiciones'
const ARTICULO_CEMENTO_ID = 'c1c2c3c4-0000-0000-0000-000000000001'

function renderPagina() {
  return render(
    <MemoryRouter>
      <AsistenteChatPage />
    </MemoryRouter>,
  )
}

async function escribirYEnviar(user: ReturnType<typeof userEvent.setup>, mensaje: string) {
  await user.type(screen.getByLabelText('Escribí tu mensaje'), mensaje)
  await user.click(screen.getByRole('button', { name: 'Enviar' }))
}

/** Cuenta los POST /api/requisiciones y guarda el último cuerpo y encabezado de idempotencia. */
function espiarCreacion() {
  const visto = { llamadas: 0, cuerpo: null as unknown, clave: null as string | null }
  server.use(
    http.post(RUTA_REQUISICIONES, async ({ request }) => {
      visto.llamadas += 1
      visto.cuerpo = await request.json()
      visto.clave = request.headers.get('Idempotency-Key')
      return HttpResponse.json(
        {
          requisicionId: 'd1d2d3d4-0000-0000-0000-000000000099',
          numeroRequisicion: 'REQ-2026-0099',
          obraId: 'b1b2c3d4-0000-0000-0000-0000000000a1',
          estado: 'Pendiente',
          fechaCreacion: new Date().toISOString(),
          items: [],
        },
        { status: 201 },
      )
    }),
  )
  return visto
}

describe('AsistenteChatPage', () => {
  it('arranca con un saludo, el botón Enviar deshabilitado y el enlace al formulario manual', () => {
    renderPagina()

    expect(screen.getByText('¿En qué te ayudo?')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Enviar' })).toBeDisabled()
    expect(screen.getByRole('link', { name: 'Cargar una requisición a mano' })).toHaveAttribute(
      'href',
      '/obra/requisicion/nueva',
    )
    expect(screen.getByRole('link', { name: 'Volver al inicio' })).toHaveAttribute('href', '/')
  })

  it('cuenta lo que sabe hacer, marca lo que todavía no anda y lo saca cuando empieza la conversación', async () => {
    const user = userEvent.setup()
    renderPagina()

    const lista = screen.getByRole('list', { name: 'Lo que puedo hacer' })
    expect(lista).toHaveTextContent('Pedir materiales')
    expect(lista).toHaveTextContent('Ver tus requisiciones')
    expect(lista).toHaveTextContent('Registrar lo que llegó')
    // Solo "Pedir materiales" funciona hoy: las otras dos llevan la etiqueta.
    expect(screen.getAllByText('Próximamente')).toHaveLength(2)

    await escribirYEnviar(user, 'necesito 20 bolsas de cemento')
    await screen.findByRole('button', { name: 'Confirmar' })

    expect(screen.queryByRole('list', { name: 'Lo que puedo hacer' })).not.toBeInTheDocument()
  })

  it('tocar "Pedir materiales" manda ese mensaje y el asistente pregunta qué necesita; las que faltan están deshabilitadas', async () => {
    const user = userEvent.setup()
    renderPagina()

    expect(screen.getByRole('button', { name: /Ver tus requisiciones/ })).toBeDisabled()
    expect(screen.getByRole('button', { name: /Registrar lo que llegó/ })).toBeDisabled()

    await user.click(screen.getByRole('button', { name: /Pedir materiales/ }))

    expect(await screen.findByText('Quiero pedir materiales')).toBeInTheDocument()
    expect(await screen.findByText(/¿Qué material necesitás y en qué cantidad\?/)).toBeInTheDocument()
    expect(screen.queryByRole('list', { name: 'Lo que puedo hacer' })).not.toBeInTheDocument()
  })

  it('si falta el material o la cantidad, el asistente pide el dato y no muestra ninguna tarjeta', async () => {
    const user = userEvent.setup()
    renderPagina()

    await escribirYEnviar(user, 'hola, necesito materiales')

    expect(await screen.findByText(/necesito el material y la cantidad/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Confirmar' })).not.toBeInTheDocument()
  })

  it('un pedido completo arma un borrador para revisar y NO crea nada todavía', async () => {
    const user = userEvent.setup()
    const creacion = espiarCreacion()
    renderPagina()

    await escribirYEnviar(user, 'necesito 20 bolsas de cemento')

    expect(await screen.findByRole('button', { name: 'Confirmar' })).toBeInTheDocument()
    expect(screen.getByText('Obra: Torre Alvear')).toBeInTheDocument()
    expect(screen.getByText('Etapa: Estructura')).toBeInTheDocument()
    expect(screen.getByText('20 bolsa · Cemento Portland Especial 50kg')).toBeInTheDocument()
    expect(creacion.llamadas).toBe(0)
  })

  it('al confirmar crea la requisición con el borrador y una clave de idempotencia', async () => {
    const user = userEvent.setup()
    const creacion = espiarCreacion()
    renderPagina()

    await escribirYEnviar(user, 'necesito 20 bolsas de cemento')
    await user.click(await screen.findByRole('button', { name: 'Confirmar' }))

    expect(await screen.findByText(/creé la requisición REQ-2026-0099/)).toBeInTheDocument()
    expect(creacion.llamadas).toBe(1)
    expect(creacion.cuerpo).toMatchObject({
      obraId: 'b1b2c3d4-0000-0000-0000-0000000000a1',
      items: [{ articuloId: ARTICULO_CEMENTO_ID, cantidad: 20 }],
    })
    expect(creacion.clave).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Confirmar' })).not.toBeInTheDocument()
  })

  it('cancelar descarta el borrador y no crea nada', async () => {
    const user = userEvent.setup()
    const creacion = espiarCreacion()
    renderPagina()

    await escribirYEnviar(user, 'necesito 20 bolsas de cemento')
    await user.click(await screen.findByRole('button', { name: 'Cancelar' }))

    expect(await screen.findByText(/descarté el borrador/)).toBeInTheDocument()
    expect(creacion.llamadas).toBe(0)
    expect(screen.queryByRole('button', { name: 'Confirmar' })).not.toBeInTheDocument()
  })

  it('si crear la requisición da 409 muestra el motivo y deja reintentar con la misma clave', async () => {
    const user = userEvent.setup()
    const claves: Array<string | null> = []
    server.use(
      http.post(RUTA_REQUISICIONES, ({ request }) => {
        claves.push(request.headers.get('Idempotency-Key'))
        return HttpResponse.json(
          { title: 'Conflicto', status: 409, detail: 'Ya existe una requisición idéntica reciente.' },
          { status: 409 },
        )
      }),
    )
    renderPagina()

    await escribirYEnviar(user, 'necesito 20 bolsas de cemento')
    await user.click(await screen.findByRole('button', { name: 'Confirmar' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Ya existe una requisición idéntica reciente.',
    )
    await user.click(screen.getByRole('button', { name: 'Confirmar' }))
    await waitFor(() => expect(claves).toHaveLength(2))
    expect(claves[0]).toBeTruthy()
    expect(claves[0]).toBe(claves[1])
  })

  it('reenvía el historial de la conversación en el siguiente mensaje', async () => {
    const user = userEvent.setup()
    const cuerpos: Array<{ mensaje: string; historial: Array<{ rol: string; texto: string }> }> = []
    server.use(
      http.post(RUTA_CHAT, async ({ request }) => {
        cuerpos.push((await request.json()) as (typeof cuerpos)[number])
        return HttpResponse.json({ respuesta: 'Contame más.', borrador: null }, { status: 200 })
      }),
    )
    renderPagina()

    await escribirYEnviar(user, 'primero')
    await screen.findByText('Contame más.')
    await escribirYEnviar(user, 'segundo')
    await waitFor(() => expect(cuerpos).toHaveLength(2))

    expect(cuerpos[0].historial).toEqual([])
    expect(cuerpos[1].mensaje).toBe('segundo')
    expect(cuerpos[1].historial).toEqual([
      { rol: 'usuario', texto: 'primero' },
      { rol: 'asistente', texto: 'Contame más.' },
    ])
  })

  it('muestra como TEXTO lo que devuelve el asistente: nada se interpreta como HTML', async () => {
    const user = userEvent.setup()
    server.use(
      http.post(RUTA_CHAT, () =>
        HttpResponse.json(
          { respuesta: '<img src=x onerror="alert(1)"> <script>alert(2)</script> hola', borrador: null },
          { status: 200 },
        ),
      ),
    )
    const { container } = renderPagina()

    await escribirYEnviar(user, 'probá algo')

    expect(await screen.findByText(/<script>alert\(2\)<\/script> hola/)).toBeInTheDocument()
    expect(container.querySelector('img')).toBeNull()
    expect(container.querySelector('script')).toBeNull()
  })

  it.each([
    [429, /ocupado/],
    [503, /no está disponible/],
    [403, /no puede usar el asistente/],
  ])('un %i del asistente se explica con un mensaje claro', async (status, texto) => {
    const user = userEvent.setup()
    server.use(
      http.post(RUTA_CHAT, () => HttpResponse.json({ title: 'Error', status }, { status })),
    )
    renderPagina()

    await escribirYEnviar(user, 'necesito 20 bolsas de cemento')

    expect(await screen.findByRole('alert')).toHaveTextContent(texto)
    expect(screen.queryByRole('button', { name: 'Confirmar' })).not.toBeInTheDocument()
  })

  it('si no hay conexión avisa y deja escribir de nuevo', async () => {
    const user = userEvent.setup()
    server.use(http.post(RUTA_CHAT, () => HttpResponse.error()))
    renderPagina()

    await escribirYEnviar(user, 'necesito 20 bolsas de cemento')

    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudo conectar con el asistente')
    expect(screen.getByLabelText('Escribí tu mensaje')).toBeEnabled()
  })

  it('no deja escribir más de 500 caracteres', () => {
    renderPagina()

    expect(screen.getByLabelText('Escribí tu mensaje')).toHaveAttribute('maxlength', '500')
  })
})
