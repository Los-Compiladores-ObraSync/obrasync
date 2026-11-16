import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it } from 'vitest'
import { reiniciarRecepcionesMock } from '../mocks/handlers'
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

  it('cuenta lo que sabe hacer, sin etiquetas de "Próximamente", y lo saca cuando empieza la conversación', async () => {
    const user = userEvent.setup()
    renderPagina()

    const lista = screen.getByRole('list', { name: 'Lo que puedo hacer' })
    expect(lista).toHaveTextContent('Pedir materiales')
    expect(lista).toHaveTextContent('Ver tus requisiciones')
    expect(lista).toHaveTextContent('Registrar lo que llegó')
    // Las tres funciones andan: ninguna lleva la etiqueta "Próximamente".
    expect(screen.queryByText('Próximamente')).not.toBeInTheDocument()

    await escribirYEnviar(user, 'necesito 20 bolsas de cemento')
    await screen.findByRole('button', { name: 'Confirmar' })

    expect(screen.queryByRole('list', { name: 'Lo que puedo hacer' })).not.toBeInTheDocument()
  })

  it('tocar "Pedir materiales" manda ese mensaje y el asistente pregunta qué necesita; las tres están habilitadas', async () => {
    const user = userEvent.setup()
    renderPagina()

    expect(screen.getByRole('button', { name: /Ver tus requisiciones/ })).toBeEnabled()
    expect(screen.getByRole('button', { name: /Registrar lo que llegó/ })).toBeEnabled()

    await user.click(screen.getByRole('button', { name: /Pedir materiales/ }))

    expect(await screen.findByText('Quiero pedir materiales')).toBeInTheDocument()
    expect(await screen.findByText(/¿Qué material necesitás y en qué cantidad\?/)).toBeInTheDocument()
    expect(screen.queryByRole('list', { name: 'Lo que puedo hacer' })).not.toBeInTheDocument()
  })

  it('tocar "Ver tus requisiciones" manda ese mensaje y el asistente cuenta en qué quedó la más nueva', async () => {
    const user = userEvent.setup()
    renderPagina()

    await user.click(screen.getByRole('button', { name: /Ver tus requisiciones/ }))

    expect(await screen.findByText('Quiero ver mis requisiciones')).toBeInTheDocument()
    expect(await screen.findByText(/La más nueva es la REQ-2026-0011/)).toBeInTheDocument()
    // Es solo una consulta: no arma ninguna tarjeta para confirmar.
    expect(screen.queryByRole('button', { name: 'Confirmar' })).not.toBeInTheDocument()
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

  it('con un borrador como el que arma el servicio real (obra sin etapa, varios ítems) lo muestra y confirma el mismo cuerpo', async () => {
    const user = userEvent.setup()
    const requisicion = {
      obraId: 'aaaaaaaa-0000-0000-0000-000000000002',
      etapa: null,
      observaciones: null,
      items: [
        { articuloId: '11111111-1111-1111-1111-111111110003', cantidad: 5 },
        { articuloId: '11111111-1111-1111-1111-111111110004', cantidad: 2.5 },
      ],
    }
    server.use(
      http.post(RUTA_CHAT, () =>
        HttpResponse.json({
          respuesta: 'Armé el borrador. Revisalo y tocá Confirmar.',
          borrador: {
            accion: 'crearRequisicion',
            titulo: 'Nueva requisición',
            resumen: {
              obra: 'Galpón Norte',
              etapa: null,
              items: [
                { descripcion: 'Cal Hidratada en Polvo (bolsa 25 kg)', cantidad: 5, unidadMedida: 'BOLSA' },
                { descripcion: 'Arena Gruesa Lavada de Río', cantidad: 2.5, unidadMedida: 'M3' },
              ],
            },
            requisicion,
          },
          tokensUsados: 1315,
        }),
      ),
    )
    const creacion = espiarCreacion()
    renderPagina()

    await escribirYEnviar(user, 'cal y arena para el galpón')

    expect(await screen.findByText(/Galpón Norte/)).toBeInTheDocument()
    expect(screen.queryByText(/Etapa:/)).not.toBeInTheDocument()
    expect(screen.getByText(/Cal Hidratada en Polvo/)).toBeInTheDocument()
    expect(screen.getByText(/Arena Gruesa Lavada de Río/)).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Confirmar' }))

    await screen.findByText(/creé la requisición REQ-2026-0099/)
    expect(creacion.llamadas).toBe(1)
    // Lo que se crea es EXACTAMENTE lo que armó el servicio, sin que el frontend le cambie nada.
    expect(creacion.cuerpo).toEqual(requisicion)
  })

  it('después de confirmar ofrece seguir: pedir otros materiales, ver las requisiciones o ir al inicio', async () => {
    const user = userEvent.setup()
    espiarCreacion()
    renderPagina()

    await escribirYEnviar(user, 'necesito 20 bolsas de cemento')
    await user.click(await screen.findByRole('button', { name: 'Confirmar' }))
    await screen.findByText(/creé la requisición REQ-2026-0099/)

    expect(screen.getByRole('button', { name: 'Pedir otros materiales' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Ver mis requisiciones' })).toHaveAttribute(
      'href',
      '/obra/requisiciones',
    )
    expect(screen.getByRole('link', { name: 'Ir al inicio' })).toHaveAttribute('href', '/')
  })

  it('"Pedir otros materiales" arranca un pedido nuevo y las opciones desaparecen', async () => {
    const user = userEvent.setup()
    const mensajes: string[] = []
    server.use(
      http.post(RUTA_CHAT, async ({ request }) => {
        const cuerpo = (await request.json()) as { mensaje: string }
        mensajes.push(cuerpo.mensaje)
        return HttpResponse.json({ respuesta: 'Dale. ¿Qué material necesitás?', borrador: null })
      }),
    )
    espiarCreacion()
    server.use(
      http.post(RUTA_CHAT, () =>
        HttpResponse.json({
          respuesta: 'Armé el borrador.',
          borrador: {
            accion: 'crearRequisicion',
            titulo: 'Nueva requisición',
            resumen: {
              obra: 'Torre Alvear',
              etapa: 'Estructura',
              items: [{ descripcion: 'Cemento', cantidad: 1, unidadMedida: 'BOLSA' }],
            },
            requisicion: {
              obraId: 'aaaaaaaa-0000-0000-0000-000000000001',
              etapa: 'Estructura',
              observaciones: null,
              items: [{ articuloId: '11111111-1111-1111-1111-111111110001', cantidad: 1 }],
            },
          },
        }),
        { once: true },
      ),
    )
    renderPagina()

    await escribirYEnviar(user, 'necesito cemento')
    await user.click(await screen.findByRole('button', { name: 'Confirmar' }))
    await user.click(await screen.findByRole('button', { name: 'Pedir otros materiales' }))

    expect(await screen.findByText('Dale. ¿Qué material necesitás?')).toBeInTheDocument()
    expect(mensajes).toEqual(['Quiero pedir materiales'])
    expect(screen.queryByRole('button', { name: 'Pedir otros materiales' })).not.toBeInTheDocument()
  })

  it('después de cancelar ofrece armar otro pedido, pero no "Ver mis requisiciones" (no se creó nada)', async () => {
    const user = userEvent.setup()
    renderPagina()

    await escribirYEnviar(user, 'necesito 20 bolsas de cemento')
    await user.click(await screen.findByRole('button', { name: 'Cancelar' }))
    await screen.findByText(/descarté el borrador/)

    expect(screen.getByRole('button', { name: 'Armar otro pedido' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Ir al inicio' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Ver mis requisiciones' })).not.toBeInTheDocument()
  })

  it('Enter envía el mensaje y Shift+Enter baja de renglón sin enviar', async () => {
    const user = userEvent.setup()
    const mensajes: string[] = []
    server.use(
      http.post(RUTA_CHAT, async ({ request }) => {
        mensajes.push(((await request.json()) as { mensaje: string }).mensaje)
        return HttpResponse.json({ respuesta: 'Contame más.', borrador: null })
      }),
    )
    renderPagina()
    const caja = screen.getByLabelText('Escribí tu mensaje')

    await user.type(caja, 'hola{Shift>}{Enter}{/Shift}chau')
    expect(mensajes).toHaveLength(0)
    expect(caja).toHaveValue('hola\nchau')

    await user.keyboard('{Enter}')

    expect(await screen.findByText('Contame más.')).toBeInTheDocument()
    expect(mensajes).toEqual(['hola\nchau'])
    expect(caja).toHaveValue('')
  })

  it('Enter con la caja vacía no envía nada', async () => {
    const user = userEvent.setup()
    const mensajes: string[] = []
    server.use(
      http.post(RUTA_CHAT, async ({ request }) => {
        mensajes.push(((await request.json()) as { mensaje: string }).mensaje)
        return HttpResponse.json({ respuesta: 'ok', borrador: null })
      }),
    )
    renderPagina()

    await user.type(screen.getByLabelText('Escribí tu mensaje'), '{Enter}')

    expect(mensajes).toHaveLength(0)
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

  it('muestra como TEXTO los nombres de la obra y de los insumos del borrador (datos del servidor) (#174)', async () => {
    const user = userEvent.setup()
    server.use(
      http.post(RUTA_CHAT, () =>
        HttpResponse.json({
          respuesta: 'Armé el borrador. Revisalo y tocá Confirmar.',
          borrador: {
            accion: 'crearRequisicion',
            titulo: 'Nueva requisición',
            resumen: {
              obra: '<script>alert(1)</script> Obra',
              etapa: null,
              items: [{ descripcion: '<img src=x onerror=alert(2)> Cemento', cantidad: 1, unidadMedida: 'BOLSA' }],
            },
            requisicion: {
              obraId: 'aaaaaaaa-0000-0000-0000-000000000002',
              etapa: null,
              observaciones: null,
              items: [{ articuloId: ARTICULO_CEMENTO_ID, cantidad: 1 }],
            },
          },
        }),
      ),
    )
    const { container } = renderPagina()

    await escribirYEnviar(user, 'necesito cemento')

    expect(await screen.findByText(/<script>alert\(1\)<\/script> Obra/)).toBeInTheDocument()
    expect(screen.getByText(/<img src=x onerror=alert\(2\)> Cemento/)).toBeInTheDocument()
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

  describe('registrar lo que llegó (#147)', () => {
    const RUTA_RECEPCIONES = '*/api/recepciones'

    /** Cuenta los POST /api/recepciones y guarda el último cuerpo. */
    function espiarRecepcion(respuesta?: () => Response) {
      const visto = { llamadas: 0, cuerpo: null as Record<string, unknown> | null }
      server.use(
        http.post(RUTA_RECEPCIONES, async ({ request }) => {
          visto.llamadas += 1
          visto.cuerpo = (await request.json()) as Record<string, unknown>
          return (
            respuesta?.() ??
            HttpResponse.json(
              {
                id: 'f1f2f3f4-0000-0000-0000-000000000001',
                obraId: 'b1b2c3d4-0000-0000-0000-0000000000a1',
                ordenCompraId: 'e1e2e3e4-0000-0000-0000-000000000001',
                numeroRemito: 'R-0001-55',
                fechaRecepcion: new Date().toISOString(),
                estado: 3,
              },
              { status: 201 },
            )
          )
        }),
      )
      return visto
    }

    beforeEach(() => reiniciarRecepcionesMock())

    it('sin remito el asistente lo pide y no muestra ninguna tarjeta', async () => {
      const user = userEvent.setup()
      renderPagina()

      await user.click(screen.getByRole('button', { name: /Registrar lo que llegó/ }))

      expect(await screen.findByText(/¿Cuál es el número de remito\?/)).toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Registrar recepción' })).not.toBeInTheDocument()
    })

    it('con el remito arma la tarjeta para revisar y NO registra nada todavía', async () => {
      const user = userEvent.setup()
      const recepcion = espiarRecepcion()
      renderPagina()

      await escribirYEnviar(user, 'llegó todo, remito R-0001-55')

      expect(await screen.findByRole('button', { name: 'Registrar recepción' })).toBeInTheDocument()
      expect(screen.getByText('Remito: R-0001-55')).toBeInTheDocument()
      expect(screen.getByText('Obra: Torre Alvear')).toBeInTheDocument()
      expect(screen.getByText(/Se registra como recibido completo y conforme/)).toBeInTheDocument()
      expect(recepcion.llamadas).toBe(0)
    })

    it('al confirmar registra la recepción total con la fecha de hoy y ofrece registrar otra', async () => {
      const user = userEvent.setup()
      const recepcion = espiarRecepcion()
      renderPagina()

      await escribirYEnviar(user, 'llegó todo, remito R-0001-55')
      await user.click(await screen.findByRole('button', { name: 'Registrar recepción' }))

      expect(await screen.findByText(/registré la recepción del remito R-0001-55/)).toBeInTheDocument()
      expect(recepcion.llamadas).toBe(1)
      const cuerpo = recepcion.cuerpo as { numeroRemito: string; fechaRecepcion: string; detalles: Record<string, unknown>[] }
      expect(cuerpo.numeroRemito).toBe('R-0001-55')
      expect(cuerpo.fechaRecepcion).toMatch(/^\d{4}-\d{2}-\d{2}T00:00:00\.000Z$/)
      expect(cuerpo.detalles.length).toBeGreaterThan(0)
      cuerpo.detalles.forEach((d) => {
        expect(d.cantidadRecibida).toBe(d.cantidadEsperada)
        expect(d.conforme).toBe(true)
      })
      expect(screen.getByRole('button', { name: 'Registrar otra recepción' })).toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Registrar recepción' })).not.toBeInTheDocument()
    })

    it('si el servidor responde 409 muestra el motivo y deja reintentar o cancelar', async () => {
      const user = userEvent.setup()
      espiarRecepcion(() =>
        HttpResponse.json(
          { title: 'Conflicto', status: 409, detail: 'Esa orden de compra ya fue recibida.' },
          { status: 409 },
        ),
      )
      renderPagina()

      await escribirYEnviar(user, 'llegó todo, remito R-0001-55')
      await user.click(await screen.findByRole('button', { name: 'Registrar recepción' }))

      expect(await screen.findByRole('alert')).toHaveTextContent('Esa orden de compra ya fue recibida.')
      expect(screen.getByRole('button', { name: 'Registrar recepción' })).toBeInTheDocument()
    })

    it('al cancelar no registra nada', async () => {
      const user = userEvent.setup()
      const recepcion = espiarRecepcion()
      renderPagina()

      await escribirYEnviar(user, 'llegó todo, remito R-0001-55')
      await screen.findByRole('button', { name: 'Registrar recepción' })
      await user.click(screen.getByRole('button', { name: 'Cancelar' }))

      expect(await screen.findByText(/no registré nada/)).toBeInTheDocument()
      expect(recepcion.llamadas).toBe(0)
    })
  })
})
