import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it } from 'vitest'
import { server } from '../mocks/server'
import { ApiError } from './ApiError'
import { apiFetch, setToken } from './client'

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
