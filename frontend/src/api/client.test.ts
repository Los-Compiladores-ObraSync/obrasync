import { delay, http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it } from 'vitest'
import { server } from '../mocks/server'
import { ApiError } from './ApiError'
import {
  apiFetch,
  apiFetchPagina,
  setToken,
  TIEMPO_LIMITE_ASISTENTE_MS,
  TIEMPO_LIMITE_MS,
} from './client'

const URL_PRUEBA = 'http://localhost:5000/api/prueba'

describe('apiFetch', () => {
  beforeEach(() => setToken(null))

  it('devuelve el JSON cuando la respuesta es exitosa', async () => {
    server.use(http.get(URL_PRUEBA, () => HttpResponse.json({ ok: true })))

    const resultado = await apiFetch<{ ok: boolean }>('/api/prueba')

    expect(resultado).toEqual({ ok: true })
  })

  it('envía el token en el encabezado Authorization', async () => {
    let recibido: string | null = null
    server.use(
      http.get(URL_PRUEBA, ({ request }) => {
        recibido = request.headers.get('Authorization')
        return HttpResponse.json({})
      }),
    )
    setToken('abc123')

    await apiFetch('/api/prueba')

    expect(recibido).toBe('Bearer abc123')
  })

  it('no envía Authorization si no hay sesión', async () => {
    let recibido: string | null = 'sin-valor'
    server.use(
      http.get(URL_PRUEBA, ({ request }) => {
        recibido = request.headers.get('Authorization')
        return HttpResponse.json({})
      }),
    )

    await apiFetch('/api/prueba')

    expect(recibido).toBeNull()
  })

  it('envía un X-Correlation-ID en cada pedido', async () => {
    let recibido: string | null = null
    server.use(
      http.get(URL_PRUEBA, ({ request }) => {
        recibido = request.headers.get('X-Correlation-ID')
        return HttpResponse.json({})
      }),
    )

    await apiFetch('/api/prueba')

    expect(recibido).toBeTruthy()
  })

  it('convierte un ProblemDetails en un ApiError', async () => {
    server.use(
      http.get(URL_PRUEBA, () =>
        HttpResponse.json(
          {
            title: 'Credenciales inválidas',
            status: 401,
            detail: 'Email o contraseña incorrectos',
            correlationId: 'corr-1',
          },
          { status: 401 },
        ),
      ),
    )

    const error = await apiFetch('/api/prueba').catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect(error).toMatchObject({
      status: 401,
      title: 'Credenciales inválidas',
      detail: 'Email o contraseña incorrectos',
      correlationId: 'corr-1',
    })
  })

  it('devuelve un ApiError aunque el error no traiga JSON', async () => {
    server.use(
      http.get(
        URL_PRUEBA,
        () => new HttpResponse('Bad Gateway', { status: 502 }),
      ),
    )

    const error = await apiFetch('/api/prueba').catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).status).toBe(502)
  })

  it('devuelve el texto cuando el cuerpo no es JSON', async () => {
    server.use(http.get(URL_PRUEBA, () => HttpResponse.text('abc-123')))

    const resultado = await apiFetch<string>('/api/prueba')

    expect(resultado).toBe('abc-123')
  })

  it('devuelve undefined cuando el cuerpo viene vacío', async () => {
    server.use(http.get(URL_PRUEBA, () => new HttpResponse(null, { status: 200 })))

    const resultado = await apiFetch<undefined>('/api/prueba')

    expect(resultado).toBeUndefined()
  })
})

describe('apiFetchPagina', () => {
  beforeEach(() => setToken(null))

  it('devuelve las filas y el total que informa X-Total-Count', async () => {
    server.use(
      http.get(URL_PRUEBA, () =>
        HttpResponse.json([{ id: 1 }, { id: 2 }], { headers: { 'X-Total-Count': '7' } }),
      ),
    )

    const pagina = await apiFetchPagina<{ id: number }>('/api/prueba')

    expect(pagina).toEqual({ items: [{ id: 1 }, { id: 2 }], total: 7 })
  })

  it('deja el total sin definir si el servicio todavía no envía el encabezado', async () => {
    server.use(http.get(URL_PRUEBA, () => HttpResponse.json([{ id: 1 }])))

    const pagina = await apiFetchPagina<{ id: number }>('/api/prueba')

    expect(pagina.items).toEqual([{ id: 1 }])
    expect(pagina.total).toBeUndefined()
  })

  it('ignora un total que no es un número', async () => {
    server.use(
      http.get(URL_PRUEBA, () => HttpResponse.json([], { headers: { 'X-Total-Count': 'muchos' } })),
    )

    const pagina = await apiFetchPagina('/api/prueba')

    expect(pagina.total).toBeUndefined()
  })

  it('devuelve una lista vacía si la respuesta no es una lista', async () => {
    server.use(http.get(URL_PRUEBA, () => new HttpResponse(null, { status: 204 })))

    const pagina = await apiFetchPagina('/api/prueba')

    expect(pagina.items).toEqual([])
  })

  it('lanza ApiError con el ProblemDetails si el servicio responde 400', async () => {
    server.use(
      http.get(URL_PRUEBA, () =>
        HttpResponse.json({ title: 'Paginación inválida', status: 400 }, { status: 400 }),
      ),
    )

    await expect(apiFetchPagina('/api/prueba')).rejects.toBeInstanceOf(ApiError)
  })
})

describe('tiempo límite de los pedidos (#199)', () => {
  beforeEach(() => setToken(null))

  it('un pedido que no responde falla con un ApiError "No pudimos conectarnos, reintentá."', async () => {
    server.use(
      http.get(URL_PRUEBA, async () => {
        await delay('infinite')
        return HttpResponse.json({})
      }),
    )

    const error = await apiFetch('/api/prueba', { tiempoLimiteMs: 50 }).catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).status).toBe(0)
    expect((error as ApiError).detail).toBe('No pudimos conectarnos, reintentá.')
  })

  it('también corta los listados paginados que no responden', async () => {
    server.use(
      http.get(URL_PRUEBA, async () => {
        await delay('infinite')
        return HttpResponse.json([])
      }),
    )

    await expect(apiFetchPagina('/api/prueba', { tiempoLimiteMs: 50 })).rejects.toMatchObject({
      status: 0,
    })
  })

  it('un pedido que responde a tiempo no se corta', async () => {
    server.use(
      http.get(URL_PRUEBA, async () => {
        await delay(20)
        return HttpResponse.json({ ok: true })
      }),
    )

    await expect(apiFetch('/api/prueba', { tiempoLimiteMs: 2000 })).resolves.toEqual({ ok: true })
  })

  it('si quien llama cancela, se propaga la cancelación y no se confunde con el tiempo agotado', async () => {
    server.use(
      http.get(URL_PRUEBA, async () => {
        await delay('infinite')
        return HttpResponse.json({})
      }),
    )
    const cancelador = new AbortController()

    const pedido = apiFetch('/api/prueba', { signal: cancelador.signal }).catch((e: unknown) => e)
    cancelador.abort()
    const error = await pedido

    expect(error).not.toBeInstanceOf(ApiError)
    expect((error as Error).name).toBe('AbortError')
  })

  it('un pedido normal espera 15 s y el del asistente más que los 45 s que le da el servidor', () => {
    expect(TIEMPO_LIMITE_MS).toBe(15_000)
    expect(TIEMPO_LIMITE_ASISTENTE_MS).toBeGreaterThan(45_000)
  })
})
