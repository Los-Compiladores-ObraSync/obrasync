import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it } from 'vitest'
import { ApiError } from '../api/ApiError'
import { setToken } from '../api/client'
import { server } from '../mocks/server'
import { loginApi } from './authService'

describe('authService.loginApi', () => {
  beforeEach(() => {
    localStorage.clear()
    setToken(null)
  })

  it('devuelve el usuario y el token con el rol primario (roles[0]) en caso exitoso', async () => {
    const respuesta = await loginApi({
      email: 'capataz@obrasync.com',
      password: 'Test1234!',
    })

    expect(respuesta.token).toBeTruthy()
    expect(respuesta.usuario).toEqual({
      id: 'a1b2c3d4-0000-0000-0000-000000000001',
      nombreCompleto: 'Juan Pérez',
      email: 'capataz@obrasync.com',
      rol: 'Capataz',
    })
  })

  it('lanza un ApiError con status 401 y detalle cuando las credenciales son incorrectas', async () => {
    const promise = loginApi({
      email: 'capataz@obrasync.com',
      password: 'password-incorrecta',
    })

    await expect(promise).rejects.toThrow(ApiError)
    await expect(promise).rejects.toMatchObject({
      status: 401,
      title: 'Credenciales inválidas',
      detail: 'Email o contraseña incorrectos.',
    })
  })

  it('falla con error cuando se produce una falla de red', async () => {
    server.use(http.post('*/api/usuarios/login', () => HttpResponse.error()))

    await expect(
      loginApi({
        email: 'capataz@obrasync.com',
        password: 'Test1234!',
      }),
    ).rejects.toThrow()
  })
})
