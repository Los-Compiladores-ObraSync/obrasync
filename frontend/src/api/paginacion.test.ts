import { describe, expect, it } from 'vitest'
import { apiFetchPagina } from './client'
import { ApiError } from './ApiError'
import { conPaginacion, hayMas, traerTodas, type PaginaDe } from './paginacion'

describe('conPaginacion', () => {
  it('agrega los parámetros con ? si la ruta no tiene otros', () => {
    expect(conPaginacion('/api/obras', 2)).toBe('/api/obras?pagina=2&tamano=50')
  })

  it('agrega los parámetros con & si la ruta ya tiene filtros', () => {
    expect(conPaginacion('/api/requisiciones?estado=Pendiente', 1, 10)).toBe(
      '/api/requisiciones?estado=Pendiente&pagina=1&tamano=10',
    )
  })
})

describe('hayMas', () => {
  it('es verdadero mientras falten filas por traer', () => {
    expect(hayMas(50, 120)).toBe(true)
  })

  it('es falso cuando ya está todo cargado', () => {
    expect(hayMas(120, 120)).toBe(false)
  })

  it('es falso si el servicio no informa el total (se comporta como antes)', () => {
    expect(hayMas(50, undefined)).toBe(false)
  })
})

describe('traerTodas', () => {
  function servicioDe(total: number, tamano: number, pedidas: number[]) {
    return async (pagina: number): Promise<PaginaDe<number>> => {
      pedidas.push(pagina)
      const desde = (pagina - 1) * tamano
      const items = Array.from({ length: Math.max(0, Math.min(tamano, total - desde)) }, (_, i) => desde + i)
      return { items, total }
    }
  }

  it('junta todas las páginas en orden', async () => {
    const pedidas: number[] = []

    const todas = await traerTodas(servicioDe(5, 2, pedidas))

    expect(todas).toEqual([0, 1, 2, 3, 4])
    expect(pedidas).toEqual([1, 2, 3])
  })

  it('pide una sola página si el servicio no informa el total', async () => {
    const pedidas: number[] = []

    const todas = await traerTodas(async (pagina) => {
      pedidas.push(pagina)
      return { items: ['a', 'b'] }
    })

    expect(todas).toEqual(['a', 'b'])
    expect(pedidas).toEqual([1])
  })

  it('corta si una página llega vacía aunque el total diga que faltan filas', async () => {
    const pedidas: number[] = []

    const todas = await traerTodas(async (pagina) => {
      pedidas.push(pagina)
      return pagina === 1 ? { items: ['a'], total: 10 } : { items: [], total: 10 }
    })

    expect(todas).toEqual(['a'])
    expect(pedidas).toEqual([1, 2])
  })

  it('no pide más de 20 páginas aunque el total nunca se alcance', async () => {
    const pedidas: number[] = []

    await traerTodas(async (pagina) => {
      pedidas.push(pagina)
      return { items: ['x'], total: 100000 }
    })

    expect(pedidas).toHaveLength(20)
  })
})

// Los mocks de MSW imitan a los servicios: estas pruebas confirman que el contrato que usa el
// frontend (parámetros, encabezados y 400) se cumple también en modo simulado.
describe('listados simulados (MSW)', () => {
  it('GET /api/obras respeta pagina y tamano y informa el total en X-Total-Count', async () => {
    const todas = await apiFetchPagina<{ id: string }>('/api/obras')
    expect(todas.total).toBeGreaterThan(0)

    const primera = await apiFetchPagina<{ id: string }>('/api/obras?pagina=1&tamano=1')

    expect(primera.items).toHaveLength(1)
    expect(primera.total).toBe(todas.total)
  })

  it('una página más allá del final devuelve una lista vacía y el mismo total', async () => {
    const pagina = await apiFetchPagina<{ id: string }>('/api/obras?pagina=99&tamano=50')

    expect(pagina.items).toEqual([])
    expect(pagina.total).toBeGreaterThan(0)
  })

  it.each(['pagina=0&tamano=10', 'pagina=1&tamano=51', 'pagina=1&tamano=0'])(
    'responde 400 con %s',
    async (consulta) => {
      await expect(apiFetchPagina(`/api/obras?${consulta}`)).rejects.toMatchObject({
        status: 400,
      })
      await expect(apiFetchPagina(`/api/obras?${consulta}`)).rejects.toBeInstanceOf(ApiError)
    },
  )
})
