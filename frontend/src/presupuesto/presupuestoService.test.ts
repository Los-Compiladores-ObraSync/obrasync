import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../api/ApiError'
import { setToken } from '../api/client'
import {
  ID_ORDEN_PRUEBA_404,
  ID_ORDEN_PRUEBA_409,
  MOCK_TOKEN_ADMIN,
  ORDEN_BLOQUEADA_1_ID,
  reiniciarPresupuestoMock,
} from '../mocks/handlers'
import * as modoSimulado from '../mocks/modoSimulado'
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

  describe('listarOrdenesBloqueadas (propuesto)', () => {
    it('devuelve lista vacía en backend real cuando no está en modo simulado', async () => {
      vi.spyOn(modoSimulado, 'estaEnModoSimulado').mockReturnValue(false)
      const ordenes = await listarOrdenesBloqueadas()
      expect(ordenes).toEqual([])
    })

    it('consulta el endpoint mock cuando está en modo simulado', async () => {
      vi.spyOn(modoSimulado, 'estaEnModoSimulado').mockReturnValue(true)
      const ordenes = await listarOrdenesBloqueadas()
      expect(ordenes.length).toBe(2)
      expect(ordenes[0].ordenCompraId).toBe(ORDEN_BLOQUEADA_1_ID)
      expect(ordenes[0].exceso).toBeGreaterThan(0)
    })
  })

  describe('reexport de formatearPesos', () => {
    it('formatea correctamente a través del reexport del servicio', () => {
      expect(formatearPesos(1000)).toMatch(/\$\s*1\.000,00/)
    })
  })
})
