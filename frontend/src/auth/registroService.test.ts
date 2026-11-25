import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { server } from '../mocks/server'
import { registrarProveedor } from './registroService'

describe('registrarProveedor', () => {
  it('envía el rol Proveedor y devuelve usuario y token', async () => {
    let cuerpo: Record<string, string> | undefined
    server.use(
      http.post('*/api/usuarios', async ({ request }) => {
        cuerpo = (await request.json()) as Record<string, string>
        return HttpResponse.json(
          {
            id: 'u-1', nombreCompleto: 'Ana López', email: 'ana@empresa.com',
            token: 'jwt-proveedor', roles: ['Proveedor', 'Capataz'],
          },
          { status: 201 },
        )
      }),
    )

    const resultado = await registrarProveedor({
      nombreCompleto: 'Ana López', email: 'ana@empresa.com', password: 'clave1234',
    })

    expect(cuerpo).toEqual({
      nombreCompleto: 'Ana López', email: 'ana@empresa.com', password: 'clave1234', rol: 'Proveedor',
    })
    expect(resultado).toEqual({
      usuario: { id: 'u-1', nombreCompleto: 'Ana López', email: 'ana@empresa.com', rol: 'Proveedor' },
      token: 'jwt-proveedor',
    })
  })
})
