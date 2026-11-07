import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { server } from '../mocks/server'
import { validarUbicacion } from './ubicacionService'

const URL_NOMINATIM = 'https://nominatim.openstreetmap.org/search'

describe('validarUbicacion', () => {
  it('devuelve "encontrada" si el mapa trae al menos un resultado', async () => {
    server.use(http.get(URL_NOMINATIM, () => HttpResponse.json([{ place_id: 1 }])))

    expect(await validarUbicacion('Av. Siempre Viva 742')).toBe('encontrada')
  })

  it('devuelve "no-encontrada" si la lista viene vacía', async () => {
    server.use(http.get(URL_NOMINATIM, () => HttpResponse.json([])))

    expect(await validarUbicacion('Calle Inexistente 1')).toBe('no-encontrada')
  })

  it('devuelve "no-disponible" con error de red o con una respuesta que no es lista', async () => {
    server.use(http.get(URL_NOMINATIM, () => HttpResponse.error()))
    expect(await validarUbicacion('Dirección A 1')).toBe('no-disponible')

    server.use(http.get(URL_NOMINATIM, () => HttpResponse.json({ error: 'raro' })))
    expect(await validarUbicacion('Dirección B 2')).toBe('no-disponible')
  })

  it('pide una sola vez la misma dirección (sin importar mayúsculas ni espacios) y la cachea', async () => {
    let consultas = 0
    server.use(
      http.get(URL_NOMINATIM, () => {
        consultas++
        return HttpResponse.json([{ place_id: 1 }])
      }),
    )

    // Dos al mismo tiempo (comparten el pedido) y una después (sale de la caché).
    const [a, b] = await Promise.all([
      validarUbicacion('Av. Siempre Viva 742'),
      validarUbicacion('  av.  siempre viva 742 '),
    ])
    const c = await validarUbicacion('AV. SIEMPRE VIVA 742')

    expect([a, b, c]).toEqual(['encontrada', 'encontrada', 'encontrada'])
    expect(consultas).toBe(1)
  })

  it('no cachea los "no-disponible": si el servicio vuelve, se reintenta', async () => {
    server.use(http.get(URL_NOMINATIM, () => HttpResponse.error()))
    expect(await validarUbicacion('Dirección C 3')).toBe('no-disponible')

    server.use(http.get(URL_NOMINATIM, () => HttpResponse.json([{ place_id: 1 }])))
    expect(await validarUbicacion('Dirección C 3')).toBe('encontrada')
  }, 10000)
})
