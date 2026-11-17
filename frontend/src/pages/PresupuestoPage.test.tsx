import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { delay, http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { setToken } from '../api/client'
import { AuthContext, type SesionUsuario } from '../auth/sessionContext'
import {
  MOCK_TOKEN_ADMIN,
  ORDEN_BLOQUEADA_1_ID,
  reiniciarPresupuestoMock,
} from '../mocks/handlers'
import * as modoSimulado from '../mocks/modoSimulado'
import { server } from '../mocks/server'
import { PresupuestoPage } from './PresupuestoPage'

const usuarioAdmin: SesionUsuario = {
  id: 'a1b2c3d4-0000-0000-0000-000000000004',
  nombreCompleto: 'Admin ObraSync',
  email: 'admin@obrasync.com',
  rol: 'Admin',
}

const usuarioComprador: SesionUsuario = {
  id: 'a1b2c3d4-0000-0000-0000-000000000002',
  nombreCompleto: 'Marta Gómez',
  email: 'comprador@obrasync.com',
  rol: 'Comprador',
}

function renderPagina(usuario: SesionUsuario = usuarioAdmin, initialEntries = ['/compras/presupuesto']) {
  return render(
    <AuthContext.Provider value={{ usuario, login: () => {}, logout: () => {} }}>
      <MemoryRouter initialEntries={initialEntries}>
        <PresupuestoPage />
      </MemoryRouter>
    </AuthContext.Provider>,
  )
}

async function seleccionarObra(user: ReturnType<typeof userEvent.setup>, nombre: string) {
  const select = await screen.findByLabelText('Obra')
  await screen.findByRole('option', { name: nombre })
  await user.selectOptions(select, nombre)
}

describe('PresupuestoPage', () => {
  beforeEach(() => {
    localStorage.clear()
    setToken(MOCK_TOKEN_ADMIN)
    reiniciarPresupuestoMock()
    vi.restoreAllMocks()
    vi.spyOn(modoSimulado, 'estaEnModoSimulado').mockReturnValue(true)
  })

  describe('Tablero de desvíos', () => {
    it('muestra el selector de obras y carga la lista con montos al elegir una obra', async () => {
      const user = userEvent.setup()
      renderPagina()

      await seleccionarObra(user, 'Torre Alvear')

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

      const headingAlbanileria = await screen.findByRole('heading', {
        level: 2,
        name: 'Albañilería',
      })
      const tarjeta = headingAlbanileria.closest('li')!

      const etiqueta = within(tarjeta).getByText('Excedida')
      expect(etiqueta).toBeInTheDocument()
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

      const alerta = await screen.findByRole('alert')
      expect(alerta).toHaveTextContent(/No se pudieron cargar las partidas presupuestarias/)

      const botonReintentar = screen.getByRole('button', { name: 'Reintentar' })
      expect(botonReintentar).toBeInTheDocument()

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

    it('tiene enlace accesible para volver a compras', () => {
      renderPagina()
      const volver = screen.getByRole('link', { name: '← Volver a compras' })
      expect(volver).toBeInTheDocument()
      expect(volver).toHaveAttribute('href', '/compras')
    })
  })

  describe('Órdenes de compra bloqueadas (#97)', () => {
    it('Admin ve las órdenes bloqueadas, el botón de autorización y puede liberarlas con motivo', async () => {
      const user = userEvent.setup()
      renderPagina(usuarioAdmin)

      expect(
        await screen.findByRole('heading', { name: 'Órdenes de compra bloqueadas' }),
      ).toBeInTheDocument()

      expect(
        screen.getAllByText(/Orden bloqueada: supera la partida de la etapa por/),
      ).toHaveLength(2)

      const botonesLiberar = screen.getAllByRole('button', { name: 'Autorizar y liberar' })
      expect(botonesLiberar.length).toBeGreaterThanOrEqual(1)

      await user.click(botonesLiberar[0])

      const textarea = screen.getByPlaceholderText('Escribí el motivo de la autorización')
      expect(textarea).toBeInTheDocument()
      expect(screen.getByText('0/500')).toBeInTheDocument()

      await user.type(textarea, 'Avance urgente')
      expect(screen.getByText('14/500')).toBeInTheDocument()

      const botonConfirmar = screen.getByRole('button', { name: 'Confirmar autorización' })
      await user.click(botonConfirmar)

      expect(await screen.findByText('Orden liberada')).toBeInTheDocument()
    })

    it('Comprador ve el aviso pero no ve el botón de autorizar', async () => {
      renderPagina(usuarioComprador)

      expect(
        await screen.findByRole('heading', { name: 'Órdenes de compra bloqueadas' }),
      ).toBeInTheDocument()

      expect(screen.queryByRole('button', { name: 'Autorizar y liberar' })).not.toBeInTheDocument()
      expect(screen.getAllByText('La autoriza un Administrador')).toHaveLength(2)
    })

    it('no muestra la sección si la lista de órdenes bloqueadas está vacía', async () => {
      server.use(
        http.get('*/api/recepciones/ordenes-bloqueadas', () => HttpResponse.json([])),
      )

      renderPagina(usuarioAdmin)

      await screen.findByLabelText('Obra')

      expect(
        screen.queryByRole('heading', { name: 'Órdenes de compra bloqueadas' }),
      ).not.toBeInTheDocument()
      expect(screen.queryByText(/no hay órdenes bloqueadas/i)).not.toBeInTheDocument()
    })

    it('muestra error 403: "Solo un Administrador puede autorizar"', async () => {
      server.use(
        http.post('*/api/recepciones/ordenes/:id/liberar', () =>
          HttpResponse.json({ title: 'Acceso denegado', status: 403 }, { status: 403 }),
        ),
      )

      const user = userEvent.setup()
      renderPagina(usuarioAdmin)

      const boton = (await screen.findAllByRole('button', { name: 'Autorizar y liberar' }))[0]
      await user.click(boton)

      await user.click(screen.getByRole('button', { name: 'Confirmar autorización' }))

      expect(
        await screen.findByText('Solo un Administrador puede autorizar'),
      ).toBeInTheDocument()
    })

    it('muestra error 404: "La orden ya no existe"', async () => {
      server.use(
        http.post('*/api/recepciones/ordenes/:id/liberar', () =>
          HttpResponse.json({ title: 'No encontrado', status: 404 }, { status: 404 }),
        ),
      )

      const user = userEvent.setup()
      renderPagina(usuarioAdmin)

      const boton = (await screen.findAllByRole('button', { name: 'Autorizar y liberar' }))[0]
      await user.click(boton)

      await user.click(screen.getByRole('button', { name: 'Confirmar autorización' }))

      expect(await screen.findByText('La orden ya no existe')).toBeInTheDocument()
    })

    it('muestra error 409: "La orden ya no está bloqueada" y la saca de la lista', async () => {
      server.use(
        http.post('*/api/recepciones/ordenes/:id/liberar', () =>
          HttpResponse.json({ title: 'Conflicto', status: 409 }, { status: 409 }),
        ),
      )

      const user = userEvent.setup()
      renderPagina(usuarioAdmin)

      const botonesIniciales = await screen.findAllByRole('button', { name: 'Autorizar y liberar' })
      expect(botonesIniciales).toHaveLength(2)

      await user.click(botonesIniciales[0])
      await user.click(screen.getByRole('button', { name: 'Confirmar autorización' }))

      expect(await screen.findByText('La orden ya no está bloqueada')).toBeInTheDocument()

      const botonesRestantes = screen.getAllByRole('button', { name: 'Autorizar y liberar' })
      expect(botonesRestantes).toHaveLength(1)
    })

    it('muestra mensaje adecuado cuando hay falla de red', async () => {
      server.use(
        http.post('*/api/recepciones/ordenes/:id/liberar', () => HttpResponse.error()),
      )

      const user = userEvent.setup()
      renderPagina(usuarioAdmin)

      const boton = (await screen.findAllByRole('button', { name: 'Autorizar y liberar' }))[0]
      await user.click(boton)

      await user.click(screen.getByRole('button', { name: 'Confirmar autorización' }))

      expect(
        await screen.findByText('No se pudo conectar con el servidor. Intentá nuevamente.'),
      ).toBeInTheDocument()
    })

    it('el doble clic manda UN solo pedido (protección contra doble submit)', async () => {
      let llamadas = 0
      server.use(
        http.post('*/api/recepciones/ordenes/:id/liberar', async () => {
          llamadas += 1
          await delay(100)
          return HttpResponse.json({
            ordenCompraId: ORDEN_BLOQUEADA_1_ID,
            obraId: 'b1b2c3d4-0000-0000-0000-0000000000a1',
            estado: 1,
            liberadaPorUsuarioId: usuarioAdmin.id,
            liberadaEn: new Date().toISOString(),
            motivoLiberacion: null,
            montoTotal: 2200000,
            partidaPresupuestariaId: null,
            montoComprometidoPartida: null,
          })
        }),
      )

      const user = userEvent.setup()
      renderPagina(usuarioAdmin)

      const boton = (await screen.findAllByRole('button', { name: 'Autorizar y liberar' }))[0]
      await user.click(boton)

      const botonConfirmar = screen.getByRole('button', { name: 'Confirmar autorización' })

      await Promise.all([user.click(botonConfirmar), user.click(botonConfirmar)])

      await screen.findByText('Orden liberada')
      expect(llamadas).toBe(1)
    })

    it('muestra el detail del servidor cuando ocurre un error 400', async () => {
      server.use(
        http.post('*/api/recepciones/ordenes/:id/liberar', () =>
          HttpResponse.json(
            {
              type: 'https://tools.ietf.org/html/rfc7231#section-6.5.1',
              title: 'Solicitud inválida',
              status: 400,
              detail: 'El motivo no puede superar los 500 caracteres.',
            },
            { status: 400 },
          ),
        ),
      )

      const user = userEvent.setup()
      renderPagina(usuarioAdmin)

      const boton = (await screen.findAllByRole('button', { name: 'Autorizar y liberar' }))[0]
      await user.click(boton)

      await user.click(screen.getByRole('button', { name: 'Confirmar autorización' }))

      expect(
        await screen.findByText('El motivo no puede superar los 500 caracteres.'),
      ).toBeInTheDocument()
    })

    it('envía el motivo como null si el campo está vacío o solo contiene espacios', async () => {
      let cuerpoEnviado: { motivo?: string | null } | null = null
      server.use(
        http.post('*/api/recepciones/ordenes/:id/liberar', async ({ request }) => {
          cuerpoEnviado = (await request.json()) as { motivo?: string | null }
          return HttpResponse.json({
            ordenCompraId: ORDEN_BLOQUEADA_1_ID,
            obraId: 'b1b2c3d4-0000-0000-0000-0000000000a1',
            estado: 1,
            liberadaPorUsuarioId: usuarioAdmin.id,
            liberadaEn: new Date().toISOString(),
            motivoLiberacion: null,
            montoTotal: 2200000,
            partidaPresupuestariaId: null,
            montoComprometidoPartida: null,
          })
        }),
      )

      const user = userEvent.setup()
      renderPagina(usuarioAdmin)

      const boton = (await screen.findAllByRole('button', { name: 'Autorizar y liberar' }))[0]
      await user.click(boton)

      const textarea = screen.getByPlaceholderText('Escribí el motivo de la autorización')
      await user.type(textarea, '   ')

      await user.click(screen.getByRole('button', { name: 'Confirmar autorización' }))

      expect(await screen.findByText('Orden liberada')).toBeInTheDocument()
      expect(cuerpoEnviado).toEqual({ motivo: null })
    })

    it('envía el texto del motivo trimeado si se ingresan espacios al inicio y final', async () => {
      let cuerpoEnviado: { motivo?: string | null } | null = null
      server.use(
        http.post('*/api/recepciones/ordenes/:id/liberar', async ({ request }) => {
          cuerpoEnviado = (await request.json()) as { motivo?: string | null }
          return HttpResponse.json({
            ordenCompraId: ORDEN_BLOQUEADA_1_ID,
            obraId: 'b1b2c3d4-0000-0000-0000-0000000000a1',
            estado: 1,
            liberadaPorUsuarioId: usuarioAdmin.id,
            liberadaEn: new Date().toISOString(),
            motivoLiberacion: 'Avance urgente',
            montoTotal: 2200000,
            partidaPresupuestariaId: null,
            montoComprometidoPartida: null,
          })
        }),
      )

      const user = userEvent.setup()
      renderPagina(usuarioAdmin)

      const boton = (await screen.findAllByRole('button', { name: 'Autorizar y liberar' }))[0]
      await user.click(boton)

      const textarea = screen.getByPlaceholderText('Escribí el motivo de la autorización')
      await user.type(textarea, '  Avance urgente  ')

      await user.click(screen.getByRole('button', { name: 'Confirmar autorización' }))

      expect(await screen.findByText('Orden liberada')).toBeInTheDocument()
      expect(cuerpoEnviado).toEqual({ motivo: 'Avance urgente' })
    })

    it('al liberar con éxito, refresca las partidas del tablero de desvíos', async () => {
      let consultasPartidas = 0
      server.use(
        http.get('*/api/presupuestos/partidas', () => {
          consultasPartidas += 1
          return HttpResponse.json([
            {
              id: 'p-1',
              obraId: 'b1b2c3d4-0000-0000-0000-0000000000a1',
              etapa: 'Estructura',
              codigoPartida: 'EST-001',
              descripcion: 'Hormigón',
              montoAsignado: 10000000,
              montoComprometido: consultasPartidas === 1 ? 6000000 : 8500000,
              montoEjecutado: 4000000,
              montoDisponible: consultasPartidas === 1 ? 4000000 : 1500000,
              moneda: 'ARS',
              createdAt: '2026-10-01T08:00:00Z',
            },
          ])
        }),
      )

      const user = userEvent.setup()
      renderPagina(usuarioAdmin)

      await seleccionarObra(user, 'Torre Alvear')
      expect((await screen.findAllByText(/\$\s*4\.000\.000,00/)).length).toBeGreaterThanOrEqual(1)

      const boton = (await screen.findAllByRole('button', { name: 'Autorizar y liberar' }))[0]
      await user.click(boton)
      await user.click(screen.getByRole('button', { name: 'Confirmar autorización' }))

      expect(await screen.findByText('Orden liberada')).toBeInTheDocument()

      const tarjetaPartida = (
        await screen.findByRole('heading', { level: 2, name: 'Estructura' })
      ).closest('li')!
      expect(
        await within(tarjetaPartida).findByText(/\$\s*1\.500\.000,00/),
      ).toBeInTheDocument()
      expect(
        await within(tarjetaPartida).findByText(/\$\s*8\.500\.000,00/),
      ).toBeInTheDocument()
      expect(consultasPartidas).toBeGreaterThanOrEqual(2)
    })

    it('no realiza la petición al endpoint de órdenes bloqueadas ni muestra la sección cuando no está en modo simulado', async () => {
      vi.spyOn(modoSimulado, 'estaEnModoSimulado').mockReturnValue(false)
      let llamadasEndpoint = 0
      server.use(
        http.get('*/api/recepciones/ordenes-bloqueadas', () => {
          llamadasEndpoint += 1
          return HttpResponse.json([])
        }),
      )

      renderPagina(usuarioAdmin)

      await screen.findByLabelText('Obra')

      expect(llamadasEndpoint).toBe(0)
      expect(
        screen.queryByRole('heading', { name: 'Órdenes de compra bloqueadas' }),
      ).not.toBeInTheDocument()
    })
  })
})
