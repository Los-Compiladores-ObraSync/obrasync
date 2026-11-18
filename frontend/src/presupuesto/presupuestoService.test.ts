import { beforeEach, describe, expect, it, vi } from 'vitest'
import { http, HttpResponse } from 'msw'
import { ApiError } from '../api/ApiError'
import { setToken } from '../api/client'
import {
  ID_ORDEN_PRUEBA_404,
  ID_ORDEN_PRUEBA_409,
  MOCK_TOKEN_ADMIN,
  MOCK_TOKEN_CAPATAZ,
  ORDEN_BLOQUEADA_1_ID,
  reiniciarPresupuestoMock,
} from '../mocks/handlers'
import * as modoSimulado from '../mocks/modoSimulado'
import { server } from '../mocks/server'
import {
  formatearPesos,
  liberarOrdenCompra,
  listarOrdenesBloqueadas,
  obtenerPartidas,
} from './presupuestoService'

describe('presupuestoService', () => {
  beforeEach(() => {
    localStorage.clear()
    setToken(MOCK_TOKEN_ADMIN)
    reiniciarPresupuestoMock()
    vi.restoreAllMocks()
  })

  describe('obtenerPartidas', () => {
    it('obtiene las partidas presupuestarias acumulando todas las páginas', async () => {
      const partidas = await obtenerPartidas()
      expect(partidas.length).toBeGreaterThanOrEqual(2)

      const estructura = partidas.find((p) => p.etapa === 'Estructura')
      const albanileria = partidas.find((p) => p.etapa === 'Albañilería')

      expect(estructura).toBeDefined()
      expect(estructura?.montoDisponible).toBeGreaterThan(0)

      expect(albanileria).toBeDefined()
      expect(albanileria?.montoDisponible).toBeLessThan(0)
    })

    it('permite filtrar partidas por obraId y etapa', async () => {
      const obraId = 'b1b2c3d4-0000-0000-0000-0000000000a1'
      const partidas = await obtenerPartidas({ obraId, etapa: 'Estructura' })

      expect(partidas).toHaveLength(1)
      expect(partidas[0].etapa).toBe('Estructura')
      expect(partidas[0].obraId).toBe(obraId)
    })

    it('conserva la paginación previa para prefijos numéricos inválidos de X-Total-Count', async () => {
      const paginas: number[] = []
      server.use(http.get('*/api/presupuestos/partidas', ({ request }) => {
        const pagina = Number(new URL(request.url).searchParams.get('pagina'))
        paginas.push(pagina)
        const items = Array.from({ length: pagina === 1 ? 50 : 1 }, (_, index) => ({
          id: `partida-${pagina}-${index}`,
          obraId: 'obra-1',
          etapa: 'Estructura',
          codigoPartida: `COD-${pagina}-${index}`,
          descripcion: '',
          montoAsignado: 100,
          montoComprometido: 0,
          montoEjecutado: 0,
          montoDisponible: 100,
          moneda: 'ARS',
          createdAt: '2026-11-18T12:00:00Z',
        }))
        return HttpResponse.json(items, { headers: { 'X-Total-Count': '51 registros' } })
      }))

      const partidas = await obtenerPartidas()
      expect(partidas).toHaveLength(51)
      expect(paginas).toEqual([1, 2])
    })

    it('mantiene el comportamiento existente cuando falta X-Total-Count en otros listados', async () => {
      const paginas: number[] = []
      server.use(http.get('*/api/presupuestos/partidas', ({ request }) => {
        paginas.push(Number(new URL(request.url).searchParams.get('pagina')))
        return HttpResponse.json([{ id: 'partida-1' }])
      }))

      const partidas = await obtenerPartidas()
      expect(partidas).toHaveLength(1)
      expect(paginas).toEqual([1])
    })
  })

  describe('liberarOrdenCompra', () => {
    it('libera la orden de compra bloqueada exitosamente (200 OK)', async () => {
      const respuesta = await liberarOrdenCompra(ORDEN_BLOQUEADA_1_ID, {
        motivo: 'Autorizado por avance de cronograma',
      })

      expect(respuesta).toMatchObject({
        ordenCompraId: ORDEN_BLOQUEADA_1_ID,
        estado: 1, // 1 = Activa
        liberadaPorUsuarioId: 'a1b2c3d4-0000-0000-0000-000000000004',
        motivoLiberacion: 'Autorizado por avance de cronograma',
      })
      expect(respuesta.liberadaEn).toBeTruthy()
      expect(respuesta.montoComprometidoPartida).toBeGreaterThan(0)
    })

    it('falla con 403 Forbidden si el usuario no tiene rol Admin', async () => {
      setToken('token-capataz')

      const promise = liberarOrdenCompra(ORDEN_BLOQUEADA_1_ID)
      await expect(promise).rejects.toThrow(ApiError)
      await expect(promise).rejects.toMatchObject({
        status: 403,
        title: 'Acceso denegado',
      })
    })

    it('falla con 404 Not Found si la orden de compra no existe', async () => {
      const promise = liberarOrdenCompra(ID_ORDEN_PRUEBA_404)
      await expect(promise).rejects.toThrow(ApiError)
      await expect(promise).rejects.toMatchObject({
        status: 404,
        title: 'No encontrado',
      })
    })

    it('falla con 409 Conflict si la orden ya no está bloqueada', async () => {
      const promise = liberarOrdenCompra(ID_ORDEN_PRUEBA_409)
      await expect(promise).rejects.toThrow(ApiError)
      await expect(promise).rejects.toMatchObject({
        status: 409,
        title: 'Conflicto',
      })
    })
  })

  describe('listarOrdenesBloqueadas', () => {
    it('consulta el endpoint autenticado también fuera del modo simulado', async () => {
      vi.spyOn(modoSimulado, 'estaEnModoSimulado').mockReturnValue(false)
      let authorization: string | null = null
      let query = ''
      server.use(
        http.get('*/api/recepciones/ordenes-bloqueadas', ({ request }) => {
          authorization = request.headers.get('Authorization')
          query = new URL(request.url).search
          return HttpResponse.json([], {
            headers: { 'X-Total-Count': '0', 'X-Page': '1', 'X-Page-Size': '50' },
          })
        }),
      )

      await expect(listarOrdenesBloqueadas()).resolves.toEqual([])
      expect(authorization).toBe(`Bearer ${MOCK_TOKEN_ADMIN}`)
      expect(query).toBe('?pagina=1&tamano=50')
    })

    it('recorre todas las páginas cuando hay más de 50 órdenes', async () => {
      const datos = Array.from({ length: 53 }, (_, index) => ({
        ordenCompraId: `orden-${index + 1}`,
        obraId: 'obra-1',
        partidaId: null,
        etapa: null,
        montoOrdenCompra: 100,
        montoAsignadoPartida: 0,
        montoComprometidoActual: 0,
        montoProyectado: 100,
        exceso: 100,
        motivo: 'Desvío',
        fechaBloqueo: '2026-11-18T12:00:00Z',
      }))
      const paginas: number[] = []
      server.use(
        http.get('*/api/recepciones/ordenes-bloqueadas', ({ request }) => {
          const url = new URL(request.url)
          const pagina = Number(url.searchParams.get('pagina'))
          const tamano = Number(url.searchParams.get('tamano'))
          paginas.push(pagina)
          return HttpResponse.json(datos.slice((pagina - 1) * tamano, pagina * tamano), {
            headers: {
              'X-Total-Count': String(datos.length),
              'X-Page': String(pagina),
              'X-Page-Size': String(tamano),
            },
          })
        }),
      )

      const ordenes = await listarOrdenesBloqueadas()

      expect(ordenes).toHaveLength(53)
      expect(paginas).toEqual([1, 2])
      expect(ordenes[0]).toMatchObject({ partidaId: null, fechaBloqueo: '2026-11-18T12:00:00Z' })
    })

    it('devuelve una lista vacía con X-Total-Count cero', async () => {
      server.use(
        http.get('*/api/recepciones/ordenes-bloqueadas', () =>
          HttpResponse.json([], { headers: { 'X-Total-Count': '0' } }),
        ),
      )
      await expect(listarOrdenesBloqueadas()).resolves.toEqual([])
    })

    it.each([undefined, '0 registros'])(
      'rechaza encabezado X-Total-Count ausente o malformado (%s)', async (header) => {
      server.use(http.get('*/api/recepciones/ordenes-bloqueadas', () =>
        HttpResponse.json([], { headers: header ? { 'X-Total-Count': header } : {} }),
      ))
      await expect(listarOrdenesBloqueadas()).rejects.toThrow(/X-Total-Count/)
      },
    )

    it('rechaza un cambio de total entre páginas', async () => {
      server.use(http.get('*/api/recepciones/ordenes-bloqueadas', ({ request }) => {
        const pagina = Number(new URL(request.url).searchParams.get('pagina'))
        const total = pagina === 1 ? 51 : 52
        return HttpResponse.json(Array.from({ length: pagina === 1 ? 50 : 2 }, (_, i) => ({ ordenCompraId: `${pagina}-${i}` })), {
          headers: { 'X-Total-Count': String(total) },
        })
      }))
      await expect(listarOrdenesBloqueadas()).rejects.toThrow(/cambió/)
    })

    it.each([
      { nombre: 'menor', count: 51, cantidad: 50 },
      { nombre: 'mayor', count: 49, cantidad: 50 },
    ])('rechaza cantidad final $nombre al total informado', async ({ count, cantidad }) => {
      server.use(http.get('*/api/recepciones/ordenes-bloqueadas', () =>
        HttpResponse.json(Array.from({ length: cantidad }, (_, i) => ({ ordenCompraId: `${i}` })), {
          headers: { 'X-Total-Count': String(count) },
        }),
      ))
      await expect(listarOrdenesBloqueadas()).rejects.toThrow(/página incompleta|inconsistente/)
    })

    it('responde 401 sin JWT y 403 para un JWT de Capataz en el handler MSW', async () => {
      setToken(null)
      await expect(listarOrdenesBloqueadas()).rejects.toMatchObject({ status: 401 })
      setToken(MOCK_TOKEN_CAPATAZ)
      await expect(listarOrdenesBloqueadas()).rejects.toMatchObject({ status: 403 })
    })

    it('propaga errores HTTP', async () => {
      server.use(
        http.get('*/api/recepciones/ordenes-bloqueadas', () =>
          HttpResponse.json({ title: 'Acceso denegado', status: 403 }, { status: 403 }),
        ),
      )
      await expect(listarOrdenesBloqueadas()).rejects.toMatchObject({ status: 403 })
    })

    it('propaga los errores de páginas posteriores sin devolver datos parciales', async () => {
      const paginas: number[] = []
      server.use(
        http.get('*/api/recepciones/ordenes-bloqueadas', ({ request }) => {
          const pagina = Number(new URL(request.url).searchParams.get('pagina'))
          paginas.push(pagina)
          if (pagina === 2) {
            return HttpResponse.json({ title: 'Error de servidor', status: 500 }, { status: 500 })
          }
          return HttpResponse.json(Array.from({ length: 50 }, (_, i) => ({ ordenCompraId: `${i}` })), {
            headers: { 'X-Total-Count': '51' },
          })
        }),
      )
      await expect(listarOrdenesBloqueadas()).rejects.toMatchObject({ status: 500 })
      expect(paginas).toEqual([1, 2])
    })

    it('no trunca silenciosamente al superar el límite de seguridad de páginas', async () => {
      server.use(
        http.get('*/api/recepciones/ordenes-bloqueadas', ({ request }) => {
          const pagina = Number(new URL(request.url).searchParams.get('pagina'))
          return HttpResponse.json(Array.from({ length: 50 }, (_, i) => ({ ordenCompraId: `orden-${pagina}-${i}` })), {
            headers: { 'X-Total-Count': '1001' },
          })
        }),
      )
      await expect(listarOrdenesBloqueadas()).rejects.toThrow(/quedó incompleta/)
    })

    it('mantiene funcionando el handler MSW del modo simulado', async () => {
      vi.spyOn(modoSimulado, 'estaEnModoSimulado').mockReturnValue(true)
      const ordenes = await listarOrdenesBloqueadas()
      expect(ordenes.length).toBe(2)
      expect(ordenes[0].ordenCompraId).toBe(ORDEN_BLOQUEADA_1_ID)
      expect(ordenes[0].exceso).toBeGreaterThan(0)
      expect(ordenes[0].partidaId).toBeTruthy()
      expect(ordenes[0].fechaBloqueo).toBeTruthy()
    })
  })

  describe('reexport de formatearPesos', () => {
    it('formatea correctamente a través del reexport del servicio', () => {
      expect(formatearPesos(1000)).toMatch(/\$\s*1\.000,00/)
    })
  })
})
